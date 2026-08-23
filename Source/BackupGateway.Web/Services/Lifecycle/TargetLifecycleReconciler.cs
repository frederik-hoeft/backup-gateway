using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Lifecycle.Transports;
using BackupGateway.Web.Services.Observability;
using BackupGateway.Web.Services.Targets;

namespace BackupGateway.Web.Services.Lifecycle;

internal sealed partial class TargetLifecycleReconciler(
    ITargetCatalog targetCatalog,
    ITargetDesiredStateProvider desiredStateProvider,
    ITargetRuntimeStateStore stateStore,
    IWakeOnLanTransport wakeOnLanTransport,
    ITargetReadinessProbe readinessProbe,
    ITargetShutdownTransport shutdownTransport,
    ILifecycleAuditWriter lifecycleAuditWriter,
    LifecycleMetrics lifecycleMetrics,
    TimeProvider timeProvider,
    ILogger<TargetLifecycleReconciler> logger) : ITargetLifecycleReconciler
{
    private const int MAXIMUM_TRANSITIONS_PER_PASS = 8;

    public async Task ReconcileAsync(string targetId, CancellationToken cancellationToken)
    {
        long started = timeProvider.GetTimestamp();
        string outcome = "success";
        try
        {
            if (!targetCatalog.TryGet(targetId, out TargetDefinition? configuredTarget) || configuredTarget is null)
            {
                LogUnconfiguredTarget(logger, targetId);
                return;
            }
            TargetDefinition target = configuredTarget;

            for (int transition = 0; transition < MAXIMUM_TRANSITIONS_PER_PASS; transition++)
            {
                TargetDesiredState desiredState = await desiredStateProvider.GetAsync(targetId, cancellationToken);
                TargetRuntimeSnapshot observation = await stateStore.GetAsync(targetId, cancellationToken);
                bool converged = desiredState == TargetDesiredState.Online
                    ? await EnsureOnlineAsync(target, observation.State, cancellationToken)
                    : await EnsureOfflineAsync(target, observation.State, cancellationToken);
                if (converged)
                {
                    return;
                }
            }

            outcome = "failure";
            await RecordFaultAsync(targetId, "transition-limit", null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (TargetLifecycleTransportException exception)
        {
            outcome = "failure";
            await RecordFaultAsync(targetId, exception.FailureCode, exception, CancellationToken.None);
        }
        catch (Exception exception)
        {
            outcome = "failure";
            await RecordFaultAsync(targetId, "unexpected-lifecycle-failure", exception, CancellationToken.None);
        }
        finally
        {
            lifecycleMetrics.Record(targetId, "reconcile", outcome, timeProvider.GetElapsedTime(started));
        }
    }

    private async Task<bool> EnsureOnlineAsync(
        TargetDefinition target,
        TargetLifecycleState observedState,
        CancellationToken cancellationToken)
    {
        if (observedState == TargetLifecycleState.Stopping)
        {
            if (!await WaitForOfflineAsync(target, target.Shutdown.OfflineTimeout, cancellationToken))
            {
                throw new TargetLifecycleTransportException(
                    "shutdown-offline-timeout",
                    "A target already marked as stopping did not become unavailable before its timeout.");
            }
            await stateStore.SetAsync(target.Id, TargetLifecycleState.Offline, cancellationToken);
            return false;
        }

        if (await readinessProbe.ProbeAsync(target, cancellationToken))
        {
            await stateStore.SetAsync(target.Id, TargetLifecycleState.Online, cancellationToken);
            return await desiredStateProvider.GetAsync(target.Id, cancellationToken) == TargetDesiredState.Online;
        }

        await stateStore.SetAsync(target.Id, TargetLifecycleState.Starting, cancellationToken);
        await ExecuteSideEffectAsync(
            target.Id,
            "wake",
            ct => wakeOnLanTransport.SendAsync(target, ct),
            cancellationToken);
        TargetDesiredState desiredAfterWake = await desiredStateProvider.GetAsync(target.Id, cancellationToken);

        bool becameReady = await WaitForOnlineAsync(target, target.Readiness.OverallTimeout, cancellationToken);
        if (!becameReady)
        {
            if (desiredAfterWake == TargetDesiredState.Offline)
            {
                await stateStore.SetAsync(target.Id, TargetLifecycleState.Offline, cancellationToken);
                return true;
            }
            throw new TargetLifecycleTransportException(
                "readiness-timeout",
                "The target did not become ready after Wake-on-LAN before its configured timeout.");
        }

        await stateStore.SetAsync(target.Id, TargetLifecycleState.Online, cancellationToken);
        return false;
    }

    private async Task<bool> EnsureOfflineAsync(
        TargetDefinition target,
        TargetLifecycleState observedState,
        CancellationToken cancellationToken)
    {
        if (observedState == TargetLifecycleState.Starting)
        {
            if (!await WaitForOnlineAsync(target, target.Readiness.OverallTimeout, cancellationToken))
            {
                await stateStore.SetAsync(target.Id, TargetLifecycleState.Offline, cancellationToken);
                return await desiredStateProvider.GetAsync(target.Id, cancellationToken) == TargetDesiredState.Offline;
            }
            await stateStore.SetAsync(target.Id, TargetLifecycleState.Online, cancellationToken);
            return false;
        }

        bool ready = await readinessProbe.ProbeAsync(target, cancellationToken);
        if (!ready)
        {
            if (observedState == TargetLifecycleState.Stopping
                && !await WaitForOfflineAsync(target, target.Shutdown.OfflineTimeout, cancellationToken))
            {
                throw new TargetLifecycleTransportException(
                    "shutdown-offline-timeout",
                    "The stopping target did not remain unavailable before its timeout.");
            }

            await stateStore.SetAsync(target.Id, TargetLifecycleState.Offline, cancellationToken);
            return await desiredStateProvider.GetAsync(target.Id, cancellationToken) == TargetDesiredState.Offline;
        }

        await stateStore.SetAsync(target.Id, TargetLifecycleState.Stopping, cancellationToken);
        if (observedState != TargetLifecycleState.Stopping
            && await desiredStateProvider.GetAsync(target.Id, cancellationToken) == TargetDesiredState.Online)
        {
            await stateStore.SetAsync(target.Id, TargetLifecycleState.Online, cancellationToken);
            return false;
        }

        await ExecuteSideEffectAsync(
            target.Id,
            "shutdown",
            ct => shutdownTransport.RequestShutdownAsync(target, ct),
            cancellationToken);
        _ = await desiredStateProvider.GetAsync(target.Id, cancellationToken);

        if (!await WaitForOfflineAsync(target, target.Shutdown.OfflineTimeout, cancellationToken))
        {
            throw new TargetLifecycleTransportException(
                "shutdown-offline-timeout",
                "The target did not become unavailable after the shutdown command before its configured timeout.");
        }

        await stateStore.SetAsync(target.Id, TargetLifecycleState.Offline, cancellationToken);
        return await desiredStateProvider.GetAsync(target.Id, cancellationToken) == TargetDesiredState.Offline;
    }

    private async Task<bool> WaitForOnlineAsync(
        TargetDefinition target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = timeProvider.GetUtcNow() + timeout;
        while (true)
        {
            if (await readinessProbe.ProbeAsync(target, cancellationToken))
            {
                return true;
            }
            if (!await DelayUntilNextProbeAsync(deadline, target.Readiness.RetryInterval, cancellationToken))
            {
                return false;
            }
        }
    }

    private async Task<bool> WaitForOfflineAsync(
        TargetDefinition target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = timeProvider.GetUtcNow() + timeout;
        bool observedUnavailable = false;
        while (true)
        {
            bool ready = await readinessProbe.ProbeAsync(target, cancellationToken);
            if (!ready)
            {
                if (observedUnavailable)
                {
                    return true;
                }
                observedUnavailable = true;
            }
            else
            {
                observedUnavailable = false;
            }

            if (!await DelayUntilNextProbeAsync(deadline, target.Shutdown.RetryInterval, cancellationToken))
            {
                return false;
            }
        }
    }

    private async Task<bool> DelayUntilNextProbeAsync(
        DateTimeOffset deadline,
        TimeSpan retryInterval,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }
        await Task.Delay(remaining < retryInterval ? remaining : retryInterval, timeProvider, cancellationToken);
        return timeProvider.GetUtcNow() < deadline;
    }

    private async Task ExecuteSideEffectAsync(
        string targetId,
        string operation,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        string eventType = $"lifecycle.{operation}";
        await lifecycleAuditWriter.WriteAsync(targetId, eventType, "intent", null, cancellationToken);
        long started = timeProvider.GetTimestamp();
        try
        {
            await action(cancellationToken);
            lifecycleMetrics.Record(targetId, operation, "success", timeProvider.GetElapsedTime(started));
            await lifecycleAuditWriter.WriteAsync(targetId, eventType, "success", null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lifecycleMetrics.Record(targetId, operation, "cancelled", timeProvider.GetElapsedTime(started));
            throw;
        }
        catch (Exception exception)
        {
            lifecycleMetrics.Record(targetId, operation, "failure", timeProvider.GetElapsedTime(started));
            string failureCode = exception is TargetLifecycleTransportException transportException
                ? transportException.FailureCode
                : "unexpected-lifecycle-failure";
            await lifecycleAuditWriter.WriteAsync(targetId, eventType, "failure", failureCode, CancellationToken.None);
            throw;
        }
    }

    private async Task RecordFaultAsync(
        string targetId,
        string failureCode,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        LogLifecycleFault(logger, exception, targetId, failureCode);
        try
        {
            await stateStore.RecordFaultAsync(targetId, failureCode, cancellationToken);
        }
        catch (Exception persistenceException)
        {
            LogFaultPersistenceFailure(logger, persistenceException, targetId, failureCode);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Skipping lifecycle reconciliation for unconfigured target {TargetId}.")]
    private static partial void LogUnconfiguredTarget(ILogger logger, string targetId);

    [LoggerMessage(LogLevel.Error, "Lifecycle reconciliation faulted for target {TargetId} with code {FailureCode}.")]
    private static partial void LogLifecycleFault(ILogger logger, Exception? exception, string targetId, string failureCode);

    [LoggerMessage(LogLevel.Critical, "Could not persist lifecycle fault for target {TargetId} with code {FailureCode}.")]
    private static partial void LogFaultPersistenceFailure(ILogger logger, Exception exception, string targetId, string failureCode);
}
