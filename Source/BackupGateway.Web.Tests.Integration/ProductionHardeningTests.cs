using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Hosting;
using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Lifecycle;
using BackupGateway.Web.Services.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class ProductionHardeningTests
{
    [TestInitialize]
    public Task InitializeAsync() => IntegrationTestDatabase.ResetAsync();

    [TestMethod]
    public async Task RestartPreservesLeaseIntentAndInvalidatesTransitionalObservationAsync()
    {
        Guid clientId = Guid.CreateVersion7();
        Guid leaseId = Guid.CreateVersion7();

        await using (ServiceProvider firstProvider = await IntegrationTestDatabase.CreateServiceProviderAsync(includeTarget: true))
        {
            await InitializeTargetStateAsync(firstProvider);
            LeaseAcquireResult acquired = await AcquireAsync(firstProvider, clientId, "backup-1", leaseId);
            Assert.IsTrue(acquired.WasCreated);

            await using AsyncServiceScope scope = firstProvider.CreateAsyncScope();
            BackupGatewayDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
            TargetRuntimeObservation observation = await dbContext.Set<TargetRuntimeObservation>()
                .SingleAsync(candidate => candidate.TargetId == "backup-1");
            observation.State = TargetLifecycleState.Stopping;
            observation.ObservedAtUtc = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1));
            await dbContext.SaveChangesAsync();
        }

        await using (ServiceProvider secondProvider = await IntegrationTestDatabase.CreateServiceProviderAsync(includeTarget: true))
        {
            Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(secondProvider, "backup-1"));
            await InitializeTargetStateAsync(secondProvider);

            await using AsyncServiceScope scope = secondProvider.CreateAsyncScope();
            BackupGatewayDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
            TargetRuntimeObservation observation = await dbContext.Set<TargetRuntimeObservation>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.TargetId == "backup-1");
            Assert.AreEqual(TargetLifecycleState.Unknown, observation.State);

            LeaseReleaseResult release = await ReleaseAsync(secondProvider, clientId, "backup-1", leaseId);
            Assert.IsTrue(release.WasReleased);
        }

        await using ServiceProvider thirdProvider = await IntegrationTestDatabase.CreateServiceProviderAsync(includeTarget: true);
        Assert.AreEqual(TargetDesiredState.Offline, await GetDesiredStateAsync(thirdProvider, "backup-1"));
        await using AsyncServiceScope finalScope = thirdProvider.CreateAsyncScope();
        BackupGatewayDbContext finalContext = finalScope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
        BackupLease persistedLease = await finalContext.Set<BackupLease>().AsNoTracking().SingleAsync(candidate => candidate.Id == leaseId);
        Assert.AreEqual(BackupLeaseState.Released, persistedLease.State);
    }

    [TestMethod]
    public async Task RandomizedConcurrentLeaseChurnCannotOverrideHeldAnchorsAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        Guid firstAnchorClient = Guid.CreateVersion7();
        Guid firstAnchorLease = Guid.CreateVersion7();
        Guid secondAnchorClient = Guid.CreateVersion7();
        Guid secondAnchorLease = Guid.CreateVersion7();
        _ = await AcquireAsync(provider, firstAnchorClient, "backup-1", firstAnchorLease);
        _ = await AcquireAsync(provider, secondAnchorClient, "backup-2", secondAnchorLease);

        Task[] workers = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            Random random = new(0x5eed + worker);
            for (int iteration = 0; iteration < 12; iteration++)
            {
                string targetId = random.Next(2) == 0 ? "backup-1" : "backup-2";
                Guid clientId = Guid.CreateVersion7();
                Guid leaseId = Guid.CreateVersion7();
                LeaseAcquireResult acquired = await AcquireAsync(provider, clientId, targetId, leaseId);
                Assert.IsTrue(acquired.WasCreated);
                Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, targetId));

                await Task.Delay(random.Next(0, 4));
                LeaseReleaseResult released = await ReleaseAsync(provider, clientId, targetId, leaseId);
                Assert.IsTrue(released.WasReleased);
                Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, targetId));
            }
        })).ToArray();

        await Task.WhenAll(workers);
        Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, "backup-1"));
        Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, "backup-2"));

        _ = await ReleaseAsync(provider, firstAnchorClient, "backup-1", firstAnchorLease);
        Assert.AreEqual(TargetDesiredState.Offline, await GetDesiredStateAsync(provider, "backup-1"));
        Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, "backup-2"));

        _ = await ReleaseAsync(provider, secondAnchorClient, "backup-2", secondAnchorLease);
        Assert.AreEqual(TargetDesiredState.Offline, await GetDesiredStateAsync(provider, "backup-2"));
    }

    [TestMethod]
    public async Task SingleInstanceGuardRejectsSecondActiveGatewayAsync()
    {
        using ConfigurationManager configuration = new();
        configuration["ConnectionStrings:DatabaseConnection"] = IntegrationTestDatabase.RequireConnectionString();
        await using SingleInstanceGuard first = new(configuration, NullLogger<SingleInstanceGuard>.Instance);
        await using SingleInstanceGuard second = new(configuration, NullLogger<SingleInstanceGuard>.Instance);

        await first.AcquireAsync(CancellationToken.None);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => second.AcquireAsync(CancellationToken.None));
        StringAssert.Contains(exception.Message, "Another active Backup Gateway instance");

        await first.DisposeAsync();
        await second.AcquireAsync(CancellationToken.None);
        Assert.IsTrue(second.IsHeld);
    }

    [TestMethod]
    public async Task CleanDatabaseMigrationIsIdempotentAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        string[] before = [.. await dbContext.Database.GetPendingMigrationsAsync()];
        Assert.AreEqual(0, before.Length);
        await dbContext.Database.MigrateAsync();
        string[] after = [.. await dbContext.Database.GetPendingMigrationsAsync()];
        Assert.AreEqual(0, after.Length);

        string[] applied = [.. await dbContext.Database.GetAppliedMigrationsAsync()];
        CollectionAssert.Contains(applied, "20260823143000_InitialPersistence");
    }

    private static async Task InitializeTargetStateAsync(ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        TargetConfigurationReconciler reconciler = scope.ServiceProvider.GetRequiredService<TargetConfigurationReconciler>();
        await reconciler.ReconcileAsync();
    }

    private static async Task<LeaseAcquireResult> AcquireAsync(
        ServiceProvider provider,
        Guid clientId,
        string targetId,
        Guid leaseId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        LeaseService leaseService = scope.ServiceProvider.GetRequiredService<LeaseService>();
        return await leaseService.AcquireAsync(clientId, targetId, leaseId, CancellationToken.None);
    }

    private static async Task<LeaseReleaseResult> ReleaseAsync(
        ServiceProvider provider,
        Guid clientId,
        string targetId,
        Guid leaseId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        LeaseService leaseService = scope.ServiceProvider.GetRequiredService<LeaseService>();
        return await leaseService.ReleaseAsync(clientId, targetId, leaseId, CancellationToken.None);
    }

    private static async Task<TargetDesiredState> GetDesiredStateAsync(ServiceProvider provider, string targetId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        TargetDesiredStateService desiredStateService = scope.ServiceProvider.GetRequiredService<TargetDesiredStateService>();
        return await desiredStateService.GetAsync(targetId);
    }
}
