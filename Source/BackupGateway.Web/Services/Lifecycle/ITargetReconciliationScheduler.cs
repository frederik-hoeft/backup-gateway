namespace BackupGateway.Web.Services.Lifecycle;

public interface ITargetReconciliationScheduler
{
    void Enqueue(string targetId);
}
