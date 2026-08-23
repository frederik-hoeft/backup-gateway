using BackupGateway.Web.Services.Targets;

namespace BackupGateway.Web.Services.Lifecycle;

internal sealed class PeriodicTargetReconciliationService(
    ITargetCatalog targetCatalog,
    ITargetReconciliationScheduler reconciliationQueue,
    LifecycleOptions options,
    TimeProvider timeProvider) : BackgroundService
{
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnqueueAll();
        using PeriodicTimer timer = new(options.ReconciliationInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            EnqueueAll();
        }
    }

    private void EnqueueAll()
    {
        foreach (TargetDefinition target in targetCatalog.All)
        {
            reconciliationQueue.Enqueue(target.Id);
        }
    }
}
