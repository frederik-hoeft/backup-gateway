using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace BackupGateway.Web.Services.Targets;

internal sealed partial class TargetConfigurationReconciler(
    BackupGatewayDbContext dbContext,
    ITargetCatalog targetCatalog,
    ILogger<TargetConfigurationReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        string[] configuredTargetIds = [.. targetCatalog.All.Select(target => target.Id)];
        string[] existingRuntimeIdValues = await dbContext.Set<TargetRuntimeObservation>()
            .AsNoTracking()
            .Select(observation => observation.TargetId)
            .ToArrayAsync(cancellationToken);
        HashSet<string> existingRuntimeIds = existingRuntimeIdValues.ToHashSet(StringComparer.Ordinal);

        foreach (string targetId in configuredTargetIds)
        {
            if (!existingRuntimeIds.Contains(targetId))
            {
                dbContext.Add(new TargetRuntimeObservation
                {
                    TargetId = targetId,
                    State = TargetLifecycleState.Unknown,
                });
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        string[] persistedRuntimeIds = await dbContext.Set<TargetRuntimeObservation>()
            .AsNoTracking()
            .Select(observation => observation.TargetId)
            .ToArrayAsync(cancellationToken);
        string[] grantTargetIds = await dbContext.Set<TargetGrant>()
            .AsNoTracking()
            .Select(grant => grant.TargetId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        HashSet<string> configured = configuredTargetIds.ToHashSet(StringComparer.Ordinal);
        int orphanedRuntimeRows = persistedRuntimeIds.Count(targetId => !configured.Contains(targetId));
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
