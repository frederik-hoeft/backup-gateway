using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Services.Lifecycle;

public sealed class TargetDesiredStateService(ITransactionService<BackupGatewayDbContext> transactionService)
{
    public Task<TargetDesiredState> GetAsync(string targetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        return transactionService.Scoped.RunReadOnlyAsync(async (dbContext, ct) =>
        {
            bool hasHeldLease = await dbContext.Set<BackupLease>()
                .AsNoTracking()
                .AnyAsync(lease => lease.TargetId == targetId && lease.State == BackupLeaseState.Held, ct);
            return hasHeldLease ? TargetDesiredState.Online : TargetDesiredState.Offline;
        }, cancellationToken);
    }
}
