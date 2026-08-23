namespace BackupGateway.Web.Services.Lifecycle;

public interface ITargetReconciliationQueue
{
    void Enqueue(string targetId);
}
