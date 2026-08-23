using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Services.Auth;

internal sealed class TargetAuthorizationService(ITransactionService<BackupGatewayDbContext> transactionService)
    : ITargetAuthorizationService
{
    public Task<bool> IsGrantedAsync(Guid clientId, string targetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        return transactionService.Scoped.RunReadOnlyAsync(
            (dbContext, ct) => dbContext.Set<TargetGrant>().AsNoTracking()
                .AnyAsync(grant => grant.ClientId == clientId && grant.TargetId == targetId, ct),
            cancellationToken);
    }
}
