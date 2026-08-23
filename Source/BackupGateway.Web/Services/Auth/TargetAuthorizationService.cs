using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Targets;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Services.Auth;

internal sealed class TargetAuthorizationService(
    ITransactionService<BackupGatewayDbContext> transactionService,
    ITargetCatalog targetCatalog)
    : ITargetAuthorizationService
{
    public Task<bool> IsGrantedAsync(Guid clientId, string targetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (!targetCatalog.TryGet(targetId, out _))
        {
            return Task.FromResult(false);
        }

        return transactionService.Scoped.RunReadOnlyAsync(
            (dbContext, ct) => dbContext.Set<TargetGrant>().AsNoTracking()
                .AnyAsync(grant => grant.ClientId == clientId && grant.TargetId == targetId, ct),
            cancellationToken);
    }
}
