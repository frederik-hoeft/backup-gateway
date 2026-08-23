namespace BackupGateway.Web.Services.Auth;

internal interface ITargetAuthorizationService
{
    Task<bool> IsGrantedAsync(Guid clientId, string targetId, CancellationToken cancellationToken = default);
}
