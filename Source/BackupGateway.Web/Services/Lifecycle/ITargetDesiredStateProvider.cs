namespace BackupGateway.Web.Services.Lifecycle;

internal interface ITargetDesiredStateProvider
{
    Task<TargetDesiredState> GetAsync(string targetId, CancellationToken cancellationToken = default);
}
