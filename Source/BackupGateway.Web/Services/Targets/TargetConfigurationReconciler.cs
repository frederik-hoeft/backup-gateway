using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace BackupGateway.Web.Services.Targets;

internal sealed partial class TargetConfigurationReconciler(
    BackupGatewayDbContext dbContext,
    ITargetCatalog targetCatalog,
    TimeProvider timeProvider,
    ILogger<TargetConfigurationReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        string[] configuredTargetIds = [.. targetCatalog.All.Select(target => target.Id)];
        Dictionary<string, TargetRuntimeObservation> runtimeObservations = await dbContext.Set<TargetRuntimeObservation>()
            .ToDictionaryAsync(observation => observation.TargetId, StringComparer.Ordinal, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (string targetId in configuredTargetIds)
        {
            if (runtimeObservations.TryGetValue(targetId, out TargetRuntimeObservation? observation))
            {
                // A persisted lifecycle state is not current evidence after a process restart. In particular,
                // retaining Starting/Stopping could cause a new process to continue a side effect that the old
                // process never reached. Startup therefore requires a fresh probe from Unknown.
                observation.State = TargetLifecycleState.Unknown;
                observation.ObservedAtUtc = now;
            }
            else
            {
                dbContext.Add(new TargetRuntimeObservation
                {
                    TargetId = targetId,
                    State = TargetLifecycleState.Unknown,
                    ObservedAtUtc = now,
                });
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        string[] grantTargetIds = await dbContext.Set<TargetGrant>()
            .AsNoTracking()
            .Select(grant => grant.TargetId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        HashSet<string> configured = configuredTargetIds.ToHashSet(StringComparer.Ordinal);
        int orphanedRuntimeRows = runtimeObservations.Keys.Count(targetId => !configured.Contains(targetId));
        int orphanedGrantTargets = grantTargetIds.Count(targetId => !configured.Contains(targetId));
        if (orphanedRuntimeRows > 0 || orphanedGrantTargets > 0)
        {
            LogOrphanedConfigurationState(logger, orphanedRuntimeRows, orphanedGrantTargets);
        }
    }

    [LoggerMessage(
        LogLevel.Warning,
        "Persisted target state references targets that are not currently configured: {RuntimeRows} runtime rows and {GrantTargets} grant target identifiers. These records remain durable but cannot authorize lifecycle operations until the target ID is configured again.")]
    private static partial void LogOrphanedConfigurationState(ILogger logger, int runtimeRows, int grantTargets);
}
