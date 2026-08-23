using BackupGateway.Web.Data;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Services.Observability;

internal sealed class LifecycleAuditWriter(
    ITransactionService<BackupGatewayDbContext> transactionService,
    IAuditEventFactory auditEventFactory) : ILifecycleAuditWriter
{
    public Task WriteAsync(
        string targetId,
        string eventType,
        string outcome,
        string? details,
        CancellationToken cancellationToken) => transactionService.Scoped.RunAsync(async (dbContext, transaction, ct) =>
    {
        dbContext.Add(auditEventFactory.Create(
            eventType,
            outcome,
            targetId: targetId,
            details: details));
        await dbContext.SaveChangesAsync(ct);
        return transaction.Commit();
    }, cancellationToken);
}
