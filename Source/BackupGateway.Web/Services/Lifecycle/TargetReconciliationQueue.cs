using BackupGateway.Web.Services.Targets;
using System.Threading.Channels;

namespace BackupGateway.Web.Services.Lifecycle;

internal sealed partial class TargetReconciliationQueue(
    TargetReconciliationCoordinator coordinator,
    ITargetCatalog targetCatalog,
    ILogger<TargetReconciliationQueue> logger)
    : BackgroundService, ITargetReconciliationQueue
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    public void Enqueue(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (!_queue.Writer.TryWrite(targetId))
        {
            LogQueueClosed(logger, targetId);
        }
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int maximumConcurrency = Math.Clamp(targetCatalog.All.Count, 1, 16);
        ParallelOptions options = new()
        {
            CancellationToken = stoppingToken,
            MaxDegreeOfParallelism = maximumConcurrency,
        };
        await Parallel.ForEachAsync(_queue.Reader.ReadAllAsync(stoppingToken), options, async (targetId, cancellationToken) =>
        {
            try
            {
                await coordinator.ReconcileAsync(targetId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                LogReconciliationFailure(logger, exception, targetId);
            }
        });
    }

    [LoggerMessage(LogLevel.Error, "Lifecycle reconciliation failed for target {TargetId}.")]
    private static partial void LogReconciliationFailure(ILogger logger, Exception exception, string targetId);

    [LoggerMessage(LogLevel.Warning, "Lifecycle reconciliation queue is closed; target {TargetId} was not scheduled.")]
    private static partial void LogQueueClosed(ILogger logger, string targetId);
}
