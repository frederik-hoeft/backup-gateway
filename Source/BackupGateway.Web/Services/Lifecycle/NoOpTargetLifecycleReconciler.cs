namespace BackupGateway.Web.Services.Lifecycle;

internal sealed class NoOpTargetLifecycleReconciler(TargetDesiredStateService desiredStateService)
    : ITargetLifecycleReconciler
{
    public async Task ReconcileAsync(string targetId, CancellationToken cancellationToken)
    {
        _ = await desiredStateService.GetAsync(targetId, cancellationToken);
    }
}
