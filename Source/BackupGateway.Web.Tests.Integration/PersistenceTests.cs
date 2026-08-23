using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class PersistenceTests
{
    [TestInitialize]
    public Task ResetDatabaseAsync() => IntegrationTestDatabase.ResetAsync();

    [TestMethod]
    public async Task DuplicateClientTargetGrantIsRejectedAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        Guid clientId = Guid.CreateVersion7();
        IdentityUser<Guid> client = new()
        {
            Id = clientId,
            UserName = "client-a",
            NormalizedUserName = "CLIENT-A",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        context.Users.Add(client);
        context.Add(new TargetGrant { ClientId = clientId, TargetId = "backup-1" });
        await context.SaveChangesAsync();

        context.Add(new TargetGrant { ClientId = clientId, TargetId = "backup-1" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ClientDeletionCascadesGrantButPreservesLeaseAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        Guid clientId = Guid.CreateVersion7();
        Guid leaseId = Guid.CreateVersion7();
        IdentityUser<Guid> client = new()
        {
            Id = clientId,
            UserName = "client-delete",
            NormalizedUserName = "CLIENT-DELETE",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        context.Users.Add(client);
        context.Add(new TargetGrant { ClientId = clientId, TargetId = "backup-1" });
        context.Add(new BackupLease
        {
            Id = leaseId,
            ClientId = clientId,
            TargetId = "backup-1",
        });
        await context.SaveChangesAsync();

        context.Users.Remove(client);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.IsFalse(await context.Set<TargetGrant>().AnyAsync(grant => grant.ClientId == clientId));
        Assert.IsTrue(await context.Set<BackupLease>().AnyAsync(lease => lease.Id == leaseId));
    }

    [TestMethod]
    public async Task ReusedBackupLeaseIdWithDifferentIdentityIsRejectedAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        Guid leaseId = Guid.CreateVersion7();
        context.Add(new BackupLease
        {
            Id = leaseId,
            ClientId = Guid.CreateVersion7(),
            TargetId = "backup-1",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.Add(new BackupLease
        {
            Id = leaseId,
            ClientId = Guid.CreateVersion7(),
            TargetId = "backup-2",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [TestMethod]
    public async Task DuplicateTargetRuntimeObservationIsRejectedAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        context.Add(new TargetRuntimeObservation { TargetId = "backup-1" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.Add(new TargetRuntimeObservation { TargetId = "backup-1" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [TestMethod]
    public async Task AuditEventUpdateAndDeleteAreRejectedAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        AuditEvent auditEvent = new()
        {
            CorrelationId = Guid.CreateVersion7(),
            EventType = "test",
            Outcome = "success",
        };
        context.Add(auditEvent);
        await context.SaveChangesAsync();

        auditEvent.Details = "modified";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        context.Entry(auditEvent).State = EntityState.Unchanged;
        context.Remove(auditEvent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [TestMethod]
    public async Task TransactionServiceCommitAndRollbackControlPersistenceAsync()
    {
        await using ServiceProvider provider = await IntegrationTestDatabase.CreateServiceProviderAsync();
        ITransactionService<BackupGatewayDbContext> transactionService =
            provider.GetRequiredService<ITransactionService<BackupGatewayDbContext>>();

        Guid committedId = Guid.CreateVersion7();
        bool committed = await transactionService.Scoped.RunAsync(async (context, transaction, cancellationToken) =>
        {
            context.Add(new TargetRuntimeObservation { Id = committedId, TargetId = "committed" });
            await context.SaveChangesAsync(cancellationToken);
            return transaction.Commit(true);
        }, CancellationToken.None);
        Assert.IsTrue(committed);

        Guid rolledBackId = Guid.CreateVersion7();
        bool rolledBack = await transactionService.Scoped.RunAsync(async (context, transaction, cancellationToken) =>
        {
            context.Add(new TargetRuntimeObservation { Id = rolledBackId, TargetId = "rolled-back" });
            await context.SaveChangesAsync(cancellationToken);
            return transaction.Rollback(false);
        }, CancellationToken.None);
        Assert.IsFalse(rolledBack);

        await using AsyncServiceScope verificationScope = provider.CreateAsyncScope();
        BackupGatewayDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
        Assert.IsTrue(await verificationContext.Set<TargetRuntimeObservation>().AnyAsync(
            observation => observation.Id == committedId));
        Assert.IsFalse(await verificationContext.Set<TargetRuntimeObservation>().AnyAsync(
            observation => observation.Id == rolledBackId));
    }
}
