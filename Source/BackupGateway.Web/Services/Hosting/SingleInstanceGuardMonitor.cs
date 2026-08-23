namespace BackupGateway.Web.Services.Hosting;

internal sealed partial class SingleInstanceGuardMonitor(
    SingleInstanceGuard guard,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider,
    ILogger<SingleInstanceGuardMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15);

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_checkInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await guard.VerifyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogGuardLost(logger, exception);
                applicationLifetime.StopApplication();
                return;
            }
        }
    }

    [LoggerMessage(LogLevel.Critical, "The PostgreSQL single-instance deployment lock can no longer be verified; stopping the gateway.")]
    private static partial void LogGuardLost(ILogger logger, Exception exception);
}
