namespace BackupGateway.Web.Services.Lifecycle;

public interface ITargetLifecycleReconciler
{
    Task ReconcileAsync(string targetId, CancellationToken cancellationToken);
}
