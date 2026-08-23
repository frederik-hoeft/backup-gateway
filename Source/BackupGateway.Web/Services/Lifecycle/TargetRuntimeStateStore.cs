using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Services.Lifecycle;

internal sealed class TargetRuntimeStateStore(
    ITransactionService<BackupGatewayDbContext> transactionService,
    TimeProvider timeProvider) : ITargetRuntimeStateStore
{
    public Task<TargetRuntimeSnapshot> GetAsync(string targetId, CancellationToken cancellationToken) =>
        transactionService.Scoped.RunReadOnlyAsync(async (dbContext, ct) =>
        {
            TargetRuntimeObservation observation = await dbContext.Set<TargetRuntimeObservation>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.TargetId == targetId, ct);
            return new TargetRuntimeSnapshot(observation.State, observation.ObservedAtUtc);
        }, cancellationToken);

    public Task SetAsync(string targetId, TargetLifecycleState state, CancellationToken cancellationToken) =>
        transactionService.Scoped.RunAsync(async (dbContext, transaction, ct) =>
        {
            TargetRuntimeObservation observation = await dbContext.Set<TargetRuntimeObservation>()
                .SingleAsync(candidate => candidate.TargetId == targetId, ct);
            observation.State = state;
            observation.ObservedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(ct);
            return transaction.Commit();
        }, cancellationToken);

    public Task RecordFaultAsync(string targetId, string failureCode, CancellationToken cancellationToken) =>
        transactionService.Scoped.RunAsync(async (dbContext, transaction, ct) =>
        {
            TargetRuntimeObservation observation = await dbContext.Set<TargetRuntimeObservation>()
                .SingleAsync(candidate => candidate.TargetId == targetId, ct);
            observation.State = TargetLifecycleState.Faulted;
            observation.ObservedAtUtc = timeProvider.GetUtcNow();
            dbContext.Add(new AuditEvent
            {
                CorrelationId = Guid.CreateVersion7(),
                TargetId = targetId,
                EventType = "lifecycle.reconciliation-failed",
                Outcome = "failure",
                Details = failureCode,
            });
            await dbContext.SaveChangesAsync(ct);
            return transaction.Commit();
        }, cancellationToken);
}
