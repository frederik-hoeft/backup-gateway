using BackupGateway.Web.Services.Observability;
using System.Collections.Concurrent;

namespace BackupGateway.Web.Services.Lifecycle;

public sealed class TargetReconciliationCoordinator(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<TargetReconciliationCoordinator> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

    public async Task ReconcileAsync(string targetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        SemaphoreSlim semaphore = _semaphores.GetOrAdd(targetId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
            CorrelationContext correlationContext = scope.ServiceProvider.GetRequiredService<CorrelationContext>();
            using IDisposable? loggingScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationContext.Id.ToString(),
                ["TargetId"] = targetId,
            });
            ITargetLifecycleReconciler reconciler = scope.ServiceProvider.GetRequiredService<ITargetLifecycleReconciler>();
            await reconciler.ReconcileAsync(targetId, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
