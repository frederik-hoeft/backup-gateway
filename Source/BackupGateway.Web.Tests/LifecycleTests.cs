using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Lifecycle;
using BackupGateway.Web.Services.Lifecycle.Transports;
using BackupGateway.Web.Services.Targets;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class LifecycleTests
{
    [TestMethod]
    public void WakeOnLanCreatesStandardMagicPacket()
    {
        byte[] mac = [0x02, 0x11, 0x22, 0x33, 0x44, 0x55];

        byte[] packet = WakeOnLanTransport.CreateMagicPacket(mac);

        Assert.AreEqual(102, packet.Length);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xff, 6).ToArray(), packet[..6]);
        for (int offset = 6; offset < packet.Length; offset += mac.Length)
        {
            CollectionAssert.AreEqual(mac, packet[offset..(offset + mac.Length)]);
        }
    }

    [TestMethod]
    public async Task ReconcilerWakesOfflineTargetAndWaitsForReadinessAsync()
    {
        Harness harness = new(TargetDesiredState.Online, TargetLifecycleState.Offline, false, true, true);

        await harness.Reconciler.ReconcileAsync("backup-1", CancellationToken.None);

        Assert.AreEqual(1, harness.Wake.Count);
        Assert.AreEqual(0, harness.Shutdown.Count);
        Assert.AreEqual(TargetLifecycleState.Online, harness.State.State);
    }

    [TestMethod]
    public async Task ReconcilerDoesNotWakeAlreadyOnlineTargetAsync()
    {
        Harness harness = new(TargetDesiredState.Online, TargetLifecycleState.Online, true);

        await harness.Reconciler.ReconcileAsync("backup-1", CancellationToken.None);

        Assert.AreEqual(0, harness.Wake.Count);
        Assert.AreEqual(0, harness.Shutdown.Count);
        Assert.AreEqual(TargetLifecycleState.Online, harness.State.State);
    }

    [TestMethod]
    public async Task ReconcilerShutsDownAfterLastLeaseAndConfirmsOfflineAsync()
    {
        Harness harness = new(TargetDesiredState.Offline, TargetLifecycleState.Online, true, false, false);

        await harness.Reconciler.ReconcileAsync("backup-1", CancellationToken.None);

        Assert.AreEqual(0, harness.Wake.Count);
        Assert.AreEqual(1, harness.Shutdown.Count);
        Assert.AreEqual(TargetLifecycleState.Offline, harness.State.State);
    }

    [TestMethod]
    public async Task ReconcilerAcquiredWhileStoppingCompletesStopBeforeWakeAsync()
    {
        Harness harness = new(TargetDesiredState.Online, TargetLifecycleState.Stopping, false, false, false, true, true);

        await harness.Reconciler.ReconcileAsync("backup-1", CancellationToken.None);

        Assert.AreEqual(1, harness.Wake.Count);
        Assert.AreEqual(0, harness.Shutdown.Count);
        Assert.AreEqual(TargetLifecycleState.Online, harness.State.State);
        CollectionAssert.Contains(harness.State.Transitions, TargetLifecycleState.Offline);
        CollectionAssert.Contains(harness.State.Transitions, TargetLifecycleState.Starting);
    }

    [TestMethod]
    public async Task ReconcilerRecordsTransportFailureAsFaultAsync()
    {
        Harness harness = new(TargetDesiredState.Offline, TargetLifecycleState.Online, true)
        {
            ShutdownFailure = new TargetLifecycleTransportException("ssh-host-key-mismatch", "test"),
        };

        await harness.Reconciler.ReconcileAsync("backup-1", CancellationToken.None);

        Assert.AreEqual(TargetLifecycleState.Faulted, harness.State.State);
        Assert.AreEqual("ssh-host-key-mismatch", harness.State.FailureCode);
    }

    [TestMethod]
    public async Task SshShutdownRejectsHostKeyMismatchBeforeSshCommandAsync()
    {
        byte[] scannedKey = Encoding.ASCII.GetBytes("scanned-key");
        string line = $"backup-1 ssh-ed25519 {Convert.ToBase64String(scannedKey)}";
        FakeProcessRunner runner = new(new ExternalProcessResult(0, line, string.Empty));
        SshShutdownTransport transport = new(runner);
        TargetDefinition target = CreateTarget() with
        {
            Shutdown = CreateTarget().Shutdown with
            {
                HostKeyFingerprint = $"SHA256:{Convert.ToBase64String(new byte[32]).TrimEnd('=')}",
            },
        };

        TargetLifecycleTransportException exception = await Assert.ThrowsAsync<TargetLifecycleTransportException>(
            () => transport.RequestShutdownAsync(target, CancellationToken.None));

        Assert.AreEqual("ssh-host-key-mismatch", exception.FailureCode);
        Assert.AreEqual(1, runner.Invocations.Count);
        Assert.AreEqual("ssh-keyscan", runner.Invocations[0].FileName);
    }

    [TestMethod]
    public void SshFingerprintUsesOpenSshSha256Format()
    {
        byte[] key = Encoding.ASCII.GetBytes("host-key");
        string line = $"backup-1 ssh-ed25519 {Convert.ToBase64String(key)}";

        bool parsed = SshShutdownTransport.TryGetFingerprint(line, out string? fingerprint);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(fingerprint);
        StringAssert.StartsWith(fingerprint, "SHA256:");
        Assert.IsFalse(fingerprint.EndsWith("=", StringComparison.Ordinal));
    }

    private static TargetDefinition CreateTarget()
    {
        string privateKeyFile = Path.Combine(Path.GetTempPath(), "backup-gateway-lifecycle-test.key");
        return new TargetDefinition(
            "backup-1",
            "10.100.100.3",
            new WakeOnLanDefinition(PhysicalAddress.Parse("02:11:22:33:44:55"), IPAddress.Parse("10.100.100.255"), 9),
            new ReadinessDefinition(22, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(20)),
            new ShutdownDefinition(
                22,
                "backup-gateway",
                "sudo /sbin/shutdown -h now",
                privateKeyFile,
                $"SHA256:{Convert.ToBase64String(new byte[32]).TrimEnd('=')}",
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(1)));
    }

    private sealed class Harness
    {
        public Harness(TargetDesiredState desiredState, TargetLifecycleState state, params bool[] readiness)
        {
            TargetDefinition target = CreateTarget();
            Catalog = new FakeTargetCatalog(target);
            Desired = new FakeDesiredStateProvider(desiredState);
            State = new FakeRuntimeStateStore(state);
            Wake = new FakeWakeOnLanTransport();
            Readiness = new FakeReadinessProbe(readiness);
            Shutdown = new FakeShutdownTransport(this);
            Reconciler = new TargetLifecycleReconciler(
                Catalog,
                Desired,
                State,
                Wake,
                Readiness,
                Shutdown,
                TimeProvider.System,
                NullLogger<TargetLifecycleReconciler>.Instance);
        }

        public FakeTargetCatalog Catalog { get; }

        public FakeDesiredStateProvider Desired { get; }

        public FakeRuntimeStateStore State { get; }

        public FakeWakeOnLanTransport Wake { get; }

        public FakeReadinessProbe Readiness { get; }

        public FakeShutdownTransport Shutdown { get; }

        public TargetLifecycleTransportException? ShutdownFailure { get; set; }

        public TargetLifecycleReconciler Reconciler { get; }
    }

    private sealed class FakeTargetCatalog(TargetDefinition target) : ITargetCatalog
    {
        public IReadOnlyCollection<TargetDefinition> All { get; } = [target];

        public bool TryGet(string targetId, out TargetDefinition? result)
        {
            result = string.Equals(targetId, target.Id, StringComparison.Ordinal) ? target : null;
            return result is not null;
        }
    }

    private sealed class FakeDesiredStateProvider(TargetDesiredState state) : ITargetDesiredStateProvider
    {
        public TargetDesiredState State { get; set; } = state;

        public Task<TargetDesiredState> GetAsync(string targetId, CancellationToken cancellationToken = default)
        {
            _ = targetId;
            _ = cancellationToken;
            return Task.FromResult(State);
        }
    }

    private sealed class FakeRuntimeStateStore(TargetLifecycleState state) : ITargetRuntimeStateStore
    {
        public TargetLifecycleState State { get; private set; } = state;

        public List<TargetLifecycleState> Transitions { get; } = [];

        public string? FailureCode { get; private set; }

        public Task<TargetRuntimeSnapshot> GetAsync(string targetId, CancellationToken cancellationToken)
        {
            _ = targetId;
            _ = cancellationToken;
            return Task.FromResult(new TargetRuntimeSnapshot(State, DateTimeOffset.UtcNow));
        }

        public Task SetAsync(string targetId, TargetLifecycleState nextState, CancellationToken cancellationToken)
        {
            _ = targetId;
            _ = cancellationToken;
            State = nextState;
            Transitions.Add(nextState);
            return Task.CompletedTask;
        }

        public Task RecordFaultAsync(string targetId, string failureCode, CancellationToken cancellationToken)
        {
            _ = targetId;
            _ = cancellationToken;
            State = TargetLifecycleState.Faulted;
            FailureCode = failureCode;
            Transitions.Add(TargetLifecycleState.Faulted);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWakeOnLanTransport : IWakeOnLanTransport
    {
        public int Count { get; private set; }

        public Task SendAsync(TargetDefinition target, CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReadinessProbe(IEnumerable<bool> results) : ITargetReadinessProbe
    {
        private readonly Queue<bool> _results = new(results);

        public Task<bool> ProbeAsync(TargetDefinition target, CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            if (_results.Count == 0)
            {
                throw new AssertFailedException("Readiness probe was called more often than expected.");
            }
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeShutdownTransport(Harness harness) : ITargetShutdownTransport
    {
        public int Count { get; private set; }

        public Task RequestShutdownAsync(TargetDefinition target, CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            Count++;
            if (harness.ShutdownFailure is not null)
            {
                throw harness.ShutdownFailure;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessRunner(params ExternalProcessResult[] results) : IExternalProcessRunner
    {
        private readonly Queue<ExternalProcessResult> _results = new(results);

        public List<ExternalProcessInvocation> Invocations { get; } = [];

        public Task<ExternalProcessResult> RunAsync(ExternalProcessInvocation invocation, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            Invocations.Add(invocation);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
