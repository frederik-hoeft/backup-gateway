using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class LeaseCoordinationTests
{
    [TestInitialize]
    public async Task InitializeAsync() => await IntegrationTestDatabase.ResetAsync();

    [TestMethod]
    public async Task RepeatedAcquisitionIsIdempotentAndConflictingReuseIsRejectedAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        Guid clientId = Guid.CreateVersion7();
        Guid otherClientId = Guid.CreateVersion7();
        Guid leaseId = Guid.CreateVersion7();

        LeaseAcquireResult first = await AcquireAsync(provider, clientId, "backup-1", leaseId);
        LeaseAcquireResult repeated = await AcquireAsync(provider, clientId, "backup-1", leaseId);
        LeaseAcquireResult conflicting = await AcquireAsync(provider, otherClientId, "backup-1", leaseId);

        Assert.IsTrue(first.WasCreated);
        Assert.IsFalse(repeated.WasCreated);
        Assert.IsFalse(repeated.IsConflict);
        Assert.IsTrue(conflicting.IsConflict);
        Assert.AreEqual(first.Lease!.CreatedAtUtc, repeated.Lease!.CreatedAtUtc);
    }

    [TestMethod]
    public async Task HeldLeaseRemainsOnlineWhenHeartbeatIsStaleAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        Guid clientId = Guid.CreateVersion7();
        Guid leaseId = Guid.CreateVersion7();
        _ = await AcquireAsync(provider, clientId, "backup-1", leaseId);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGateway.Web.Data.BackupGatewayDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<BackupGateway.Web.Data.BackupGatewayDbContext>();
        BackupGateway.Web.Data.Model.BackupLease lease = await dbContext.Set<BackupGateway.Web.Data.Model.BackupLease>().FindAsync(leaseId)
            ?? throw new InvalidOperationException("Lease was not persisted.");
        lease.LastHeartbeatAtUtc = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(1));
        await dbContext.SaveChangesAsync();

        TargetDesiredStateService desiredStateService = scope.ServiceProvider.GetRequiredService<TargetDesiredStateService>();
        TargetDesiredState desiredState = await desiredStateService.GetAsync("backup-1");

        Assert.AreEqual(TargetDesiredState.Online, desiredState);
    }

    [TestMethod]
    public async Task ConcurrentClientsKeepTargetOnlineUntilLastReleaseAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        Guid firstClient = Guid.CreateVersion7();
        Guid secondClient = Guid.CreateVersion7();
        Guid firstLease = Guid.CreateVersion7();
        Guid secondLease = Guid.CreateVersion7();

        await Task.WhenAll(
            AcquireAsync(provider, firstClient, "backup-1", firstLease),
            AcquireAsync(provider, secondClient, "backup-1", secondLease));

        Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, "backup-1"));

        await ReleaseAsync(provider, firstClient, "backup-1", firstLease);
        Assert.AreEqual(TargetDesiredState.Online, await GetDesiredStateAsync(provider, "backup-1"));

        await ReleaseAsync(provider, secondClient, "backup-1", secondLease);
        Assert.AreEqual(TargetDesiredState.Offline, await GetDesiredStateAsync(provider, "backup-1"));
    }

    private static async Task<LeaseAcquireResult> AcquireAsync(
        ServiceProvider provider,
        Guid clientId,
        string targetId,
        Guid leaseId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        LeaseService service = scope.ServiceProvider.GetRequiredService<LeaseService>();
        return await service.AcquireAsync(clientId, targetId, leaseId, CancellationToken.None);
    }

    private static async Task ReleaseAsync(
        ServiceProvider provider,
        Guid clientId,
        string targetId,
        Guid leaseId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        LeaseService service = scope.ServiceProvider.GetRequiredService<LeaseService>();
        LeaseReleaseResult result = await service.ReleaseAsync(clientId, targetId, leaseId, CancellationToken.None);
        Assert.IsTrue(result.WasReleased);
    }

    private static async Task<TargetDesiredState> GetDesiredStateAsync(ServiceProvider provider, string targetId)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        TargetDesiredStateService service = scope.ServiceProvider.GetRequiredService<TargetDesiredStateService>();
        return await service.GetAsync(targetId);
    }
}
