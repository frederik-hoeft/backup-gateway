using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Services.Leases;

public sealed class LeaseService(
    ITransactionService<BackupGatewayDbContext> transactionService,
    TargetLeaseMutationSerializer mutationSerializer,
    ITargetReconciliationQueue reconciliationQueue,
    LeaseOptions options,
    TimeProvider timeProvider)
{
    internal Task<LeaseAcquireResult> AcquireAsync(
        Guid clientId,
        string targetId,
        Guid leaseId,
        CancellationToken cancellationToken) => mutationSerializer.RunAsync(targetId, async ct =>
    {
        LeaseAcquireResult result = await transactionService.Scoped.RunAsync(async (dbContext, transaction, transactionCancellationToken) =>
        {
            BackupLease? existing = await dbContext.Set<BackupLease>()
                .SingleOrDefaultAsync(lease => lease.Id == leaseId, transactionCancellationToken);
            if (existing is not null)
            {
                if (existing.ClientId != clientId || !string.Equals(existing.TargetId, targetId, StringComparison.Ordinal))
                {
                    return transaction.Rollback(LeaseAcquireResult.Conflict());
                }
                LeaseSnapshot existingSnapshot = await CreateSnapshotAsync(dbContext, existing, transactionCancellationToken);
                return transaction.Commit(LeaseAcquireResult.Existing(existingSnapshot));
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            BackupLease lease = new()
            {
                Id = leaseId,
                ClientId = clientId,
                TargetId = targetId,
                State = BackupLeaseState.Held,
                CreatedAtUtc = now,
                LastHeartbeatAtUtc = now,
            };
            dbContext.Add(lease);
            dbContext.Add(CreateAuditEvent(clientId, targetId, leaseId, "lease.acquired"));
            await dbContext.SaveChangesAsync(transactionCancellationToken);
            LeaseSnapshot snapshot = await CreateSnapshotAsync(dbContext, lease, transactionCancellationToken);
            return transaction.Commit(LeaseAcquireResult.Created(snapshot));
        }, ct);

        if (result.WasCreated)
        {
            reconciliationQueue.Enqueue(targetId);
        }
        return result;
    }, cancellationToken);

    internal Task<LeaseSnapshot?> GetAsync(
        Guid clientId,
        string targetId,
        Guid leaseId,
        CancellationToken cancellationToken) => transactionService.Scoped.RunReadOnlyAsync(async (dbContext, ct) =>
    {
        BackupLease? lease = await dbContext.Set<BackupLease>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == leaseId
                && candidate.ClientId == clientId
                && candidate.TargetId == targetId, ct);
        return lease is null ? null : await CreateSnapshotAsync(dbContext, lease, ct);
    }, cancellationToken);

    internal Task<LeaseHeartbeatResult> HeartbeatAsync(
        Guid clientId,
        string targetId,
        Guid leaseId,
        CancellationToken cancellationToken) => mutationSerializer.RunAsync(targetId, ct =>
        transactionService.Scoped.RunAsync(async (dbContext, transaction, transactionCancellationToken) =>
        {
            BackupLease? lease = await FindOwnedLeaseAsync(dbContext, clientId, targetId, leaseId, transactionCancellationToken);
            if (lease is null)
            {
                return transaction.Rollback(LeaseHeartbeatResult.NotFound());
            }
            if (lease.State != BackupLeaseState.Held)
            {
                LeaseSnapshot terminalSnapshot = await CreateSnapshotAsync(dbContext, lease, transactionCancellationToken);
                return transaction.Commit(LeaseHeartbeatResult.NotHeld(terminalSnapshot));
            }

            lease.LastHeartbeatAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(transactionCancellationToken);
            LeaseSnapshot snapshot = await CreateSnapshotAsync(dbContext, lease, transactionCancellationToken);
            return transaction.Commit(LeaseHeartbeatResult.Updated(snapshot));
        }, ct), cancellationToken);

    internal Task<LeaseReleaseResult> ReleaseAsync(
        Guid clientId,
        string targetId,
        Guid leaseId,
        CancellationToken cancellationToken) => mutationSerializer.RunAsync(targetId, async ct =>
    {
        LeaseReleaseResult result = await transactionService.Scoped.RunAsync(async (dbContext, transaction, transactionCancellationToken) =>
        {
            BackupLease? lease = await FindOwnedLeaseAsync(dbContext, clientId, targetId, leaseId, transactionCancellationToken);
            if (lease is null)
            {
                return transaction.Rollback(LeaseReleaseResult.NotFound());
            }
            if (lease.State != BackupLeaseState.Held)
            {
                return transaction.Commit(LeaseReleaseResult.AlreadyReleased());
            }

            lease.State = BackupLeaseState.Released;
            lease.ReleasedAtUtc = timeProvider.GetUtcNow();
            dbContext.Add(CreateAuditEvent(clientId, targetId, leaseId, "lease.released"));
            await dbContext.SaveChangesAsync(transactionCancellationToken);
            return transaction.Commit(LeaseReleaseResult.Released());
        }, ct);

        if (result.WasReleased)
        {
            reconciliationQueue.Enqueue(targetId);
        }
        return result;
    }, cancellationToken);

    internal Task<LeaseReleaseResult> ForceReleaseAsync(
        Guid administratorId,
        string targetId,
        Guid leaseId,
        CancellationToken cancellationToken) => mutationSerializer.RunAsync(targetId, async ct =>
    {
        LeaseReleaseResult result = await transactionService.Scoped.RunAsync(async (dbContext, transaction, transactionCancellationToken) =>
        {
            BackupLease? lease = await dbContext.Set<BackupLease>()
                .SingleOrDefaultAsync(candidate => candidate.Id == leaseId && candidate.TargetId == targetId, transactionCancellationToken);
            if (lease is null)
            {
                return transaction.Rollback(LeaseReleaseResult.NotFound());
            }
            if (lease.State != BackupLeaseState.Held)
            {
                return transaction.Commit(LeaseReleaseResult.AlreadyReleased());
            }

            lease.State = BackupLeaseState.ForceReleased;
            lease.ReleasedAtUtc = timeProvider.GetUtcNow();
            dbContext.Add(new AuditEvent
            {
                CorrelationId = Guid.CreateVersion7(),
                ActorClientId = administratorId,
                SubjectClientId = lease.ClientId,
                TargetId = targetId,
                LeaseId = leaseId,
                EventType = "lease.force-released",
                Outcome = "success",
            });
            await dbContext.SaveChangesAsync(transactionCancellationToken);
            return transaction.Commit(LeaseReleaseResult.Released());
        }, ct);

        if (result.WasReleased)
        {
            reconciliationQueue.Enqueue(targetId);
        }
        return result;
    }, cancellationToken);

    internal bool IsStale(LeaseSnapshot lease) =>
        lease.State == BackupLeaseState.Held && timeProvider.GetUtcNow() - lease.LastHeartbeatAtUtc > options.StaleAfter;

    private static Task<BackupLease?> FindOwnedLeaseAsync(
        BackupGatewayDbContext dbContext,
        Guid clientId,
        string targetId,
        Guid leaseId,
        CancellationToken cancellationToken) => dbContext.Set<BackupLease>()
        .SingleOrDefaultAsync(candidate => candidate.Id == leaseId
            && candidate.ClientId == clientId
            && candidate.TargetId == targetId, cancellationToken);

    private static async Task<LeaseSnapshot> CreateSnapshotAsync(
        BackupGatewayDbContext dbContext,
        BackupLease lease,
        CancellationToken cancellationToken)
    {
        TargetRuntimeObservation? observation = await dbContext.Set<TargetRuntimeObservation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TargetId == lease.TargetId, cancellationToken);
        return new LeaseSnapshot(
            lease.Id,
            lease.ClientId,
            lease.TargetId,
            lease.State,
            lease.CreatedAtUtc,
            lease.LastHeartbeatAtUtc,
            lease.ReleasedAtUtc,
            observation?.State ?? TargetLifecycleState.Unknown,
            observation?.ObservedAtUtc);
    }

    private static AuditEvent CreateAuditEvent(Guid clientId, string targetId, Guid leaseId, string eventType) => new()
    {
        CorrelationId = Guid.CreateVersion7(),
        ActorClientId = clientId,
        TargetId = targetId,
        LeaseId = leaseId,
        EventType = eventType,
        Outcome = "success",
    };
}

internal sealed record LeaseSnapshot(
    Guid Id,
    Guid ClientId,
    string TargetId,
    BackupLeaseState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    TargetLifecycleState TargetState,
    DateTimeOffset? TargetObservedAtUtc);

internal sealed record LeaseAcquireResult(LeaseSnapshot? Lease, bool WasCreated, bool IsConflict)
{
    public static LeaseAcquireResult Created(LeaseSnapshot lease) => new(lease, true, false);

    public static LeaseAcquireResult Existing(LeaseSnapshot lease) => new(lease, false, false);

    public static LeaseAcquireResult Conflict() => new(null, false, true);
}

internal sealed record LeaseHeartbeatResult(LeaseSnapshot? Lease, bool IsNotFound, bool IsNotHeld)
{
    public static LeaseHeartbeatResult Updated(LeaseSnapshot lease) => new(lease, false, false);

    public static LeaseHeartbeatResult NotFound() => new(null, true, false);

    public static LeaseHeartbeatResult NotHeld(LeaseSnapshot lease) => new(lease, false, true);
}

internal sealed record LeaseReleaseResult(bool IsNotFound, bool WasReleased)
{
    public static LeaseReleaseResult NotFound() => new(true, false);

    public static LeaseReleaseResult Released() => new(false, true);

    public static LeaseReleaseResult AlreadyReleased() => new(false, false);
}
