using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Lifecycle;
using BackupGateway.Web.Services.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class LeaseCoordinationTests
{
    [TestMethod]
    public async Task TargetLeaseMutationSerializerSerializesSameTargetAsync()
    {
        TargetLeaseMutationSerializer serializer = new();
        TaskCompletionSource firstEntered = CreateSignal();
        TaskCompletionSource releaseFirst = CreateSignal();
        int concurrent = 0;
        int maximumConcurrent = 0;

        Task first = serializer.RunAsync("backup-1", async cancellationToken =>
        {
            TrackEntry(ref concurrent, ref maximumConcurrent);
            firstEntered.SetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrent);
            return true;
        }, CancellationToken.None);

        await firstEntered.Task;
        Task second = serializer.RunAsync("backup-1", async cancellationToken =>
        {
            TrackEntry(ref concurrent, ref maximumConcurrent);
            await Task.Yield();
            Interlocked.Decrement(ref concurrent);
            return true;
        }, CancellationToken.None);

        await Task.Delay(25);
        Assert.AreEqual(1, Volatile.Read(ref maximumConcurrent));

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, maximumConcurrent);
    }

    [TestMethod]
    public async Task TargetLeaseMutationSerializerAllowsDifferentTargetsAsync()
    {
        TargetLeaseMutationSerializer serializer = new();
        TaskCompletionSource bothEntered = CreateSignal();
        TaskCompletionSource release = CreateSignal();
        int concurrent = 0;
        int maximumConcurrent = 0;

        async Task<bool> RunAsync(string targetId, CancellationToken cancellationToken)
        {
            TrackEntry(ref concurrent, ref maximumConcurrent);
            if (Volatile.Read(ref concurrent) == 2)
            {
                bothEntered.TrySetResult();
            }
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrent);
            return true;
        }

        Task<bool> first = serializer.RunAsync("backup-1", ct => RunAsync("backup-1", ct), CancellationToken.None);
        Task<bool> second = serializer.RunAsync("backup-2", ct => RunAsync("backup-2", ct), CancellationToken.None);

        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.AreEqual(2, maximumConcurrent);
    }

    [TestMethod]
    public async Task ReconciliationCoordinatorSerializesSameTargetAsync()
    {
        ConcurrencyProbeReconciler reconciler = new(expectedConcurrentEntries: 1);
        await using ServiceProvider provider = CreateCoordinatorServices(reconciler);
        TargetReconciliationCoordinator coordinator = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TargetReconciliationCoordinator>.Instance);

        Task first = coordinator.ReconcileAsync("backup-1", CancellationToken.None);
        await reconciler.ExpectedEntriesReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task second = coordinator.ReconcileAsync("backup-1", CancellationToken.None);

        await Task.Delay(25);
        Assert.AreEqual(1, reconciler.MaximumConcurrent);

        reconciler.Release.SetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, reconciler.MaximumConcurrent);
    }

    [TestMethod]
    public async Task ReconciliationCoordinatorAllowsDifferentTargetsAsync()
    {
        ConcurrencyProbeReconciler reconciler = new(expectedConcurrentEntries: 2);
        await using ServiceProvider provider = CreateCoordinatorServices(reconciler);
        TargetReconciliationCoordinator coordinator = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TargetReconciliationCoordinator>.Instance);

        Task first = coordinator.ReconcileAsync("backup-1", CancellationToken.None);
        Task second = coordinator.ReconcileAsync("backup-2", CancellationToken.None);

        await reconciler.ExpectedEntriesReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(2, reconciler.MaximumConcurrent);

        reconciler.Release.SetResult();
        await Task.WhenAll(first, second);
    }

    [TestMethod]
    public void LeaseOptionsRejectUnsafeStaleWindow()
    {
        using ConfigurationManager configuration = new();
        configuration["Leases:StaleAfter"] = "00:00:30";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LeaseOptions.FromConfiguration(configuration));

        StringAssert.Contains(exception.Message, "Leases:StaleAfter");
    }

    private static ServiceProvider CreateCoordinatorServices(ConcurrencyProbeReconciler reconciler)
    {
        ServiceCollection services = new();
        services.AddScoped<CorrelationContext>();
        services.AddSingleton(reconciler);
        services.AddSingleton<ITargetLifecycleReconciler>(provider =>
            provider.GetRequiredService<ConcurrencyProbeReconciler>());
        return services.BuildServiceProvider();
    }

    private sealed class ConcurrencyProbeReconciler(int expectedConcurrentEntries) : ITargetLifecycleReconciler
    {
        private int _concurrent;
        private int _maximumConcurrent;
        private int _entries;

        public TaskCompletionSource ExpectedEntriesReached { get; } = CreateSignal();

        public TaskCompletionSource Release { get; } = CreateSignal();

        public int MaximumConcurrent => Volatile.Read(ref _maximumConcurrent);

        public async Task ReconcileAsync(string targetId, CancellationToken cancellationToken)
        {
            _ = targetId;
            TrackEntry(ref _concurrent, ref _maximumConcurrent);
            if (Interlocked.Increment(ref _entries) >= expectedConcurrentEntries)
            {
                ExpectedEntriesReached.TrySetResult();
            }

            await Release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref _concurrent);
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void TrackEntry(ref int concurrent, ref int maximumConcurrent)
    {
        int current = Interlocked.Increment(ref concurrent);
        int observed;
        do
        {
            observed = Volatile.Read(ref maximumConcurrent);
            if (current <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximumConcurrent, current, observed) != observed);
    }
}
