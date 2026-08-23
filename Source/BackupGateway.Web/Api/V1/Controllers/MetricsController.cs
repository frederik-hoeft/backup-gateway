using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Observability;
using BackupGateway.Web.Services.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("metrics")]
[AllowAnonymous]
public sealed class MetricsController(
    ITransactionService<BackupGatewayDbContext> transactionService,
    ITargetCatalog targetCatalog,
    LifecycleMetrics lifecycleMetrics,
    LeaseOptions leaseOptions,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Produces("text/plain")]
    public Task<IActionResult> GetAsync(CancellationToken cancellationToken) =>
        transactionService.Scoped.RunReadOnlyAsync<IActionResult>(async (dbContext, ct) =>
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            List<BackupLease> heldLeases = await dbContext.Set<BackupLease>()
                .AsNoTracking()
                .Where(lease => lease.State == BackupLeaseState.Held)
                .ToListAsync(ct);
            Dictionary<string, TargetRuntimeObservation> observations = await dbContext.Set<TargetRuntimeObservation>()
                .AsNoTracking()
                .ToDictionaryAsync(observation => observation.TargetId, StringComparer.Ordinal, ct);

            StringBuilder output = new();
            output.AppendLine("# HELP backup_gateway_held_leases Number of currently held leases by target and freshness.");
            output.AppendLine("# TYPE backup_gateway_held_leases gauge");
            foreach (TargetDefinition target in targetCatalog.All.OrderBy(target => target.Id, StringComparer.Ordinal))
            {
                int fresh = heldLeases.Count(lease => lease.TargetId == target.Id && now - lease.LastHeartbeatAtUtc <= leaseOptions.StaleAfter);
                int stale = heldLeases.Count(lease => lease.TargetId == target.Id && now - lease.LastHeartbeatAtUtc > leaseOptions.StaleAfter);
                AppendMetric(output, "backup_gateway_held_leases", target.Id, "fresh", fresh);
                AppendMetric(output, "backup_gateway_held_leases", target.Id, "stale", stale);
            }

            output.AppendLine("# HELP backup_gateway_target_state Current observed lifecycle state as a one-hot gauge.");
            output.AppendLine("# TYPE backup_gateway_target_state gauge");
            foreach (TargetDefinition target in targetCatalog.All.OrderBy(target => target.Id, StringComparer.Ordinal))
            {
                TargetLifecycleState current = observations.TryGetValue(target.Id, out TargetRuntimeObservation? observation)
                    ? observation.State
                    : TargetLifecycleState.Unknown;
                foreach (TargetLifecycleState state in Enum.GetValues<TargetLifecycleState>())
                {
                    output.Append("backup_gateway_target_state{target=\"")
                        .Append(target.Id)
                        .Append("\",state=\"")
                        .Append(GetMetricStateName(state))
                        .Append("\"} ")
                        .AppendLine(state == current ? "1" : "0");
                }
            }

            output.AppendLine("# HELP backup_gateway_lifecycle_operation_total Lifecycle operation outcomes since process start.");
            output.AppendLine("# TYPE backup_gateway_lifecycle_operation_total counter");
            output.AppendLine("# HELP backup_gateway_lifecycle_operation_duration_seconds Total lifecycle operation duration since process start.");
            output.AppendLine("# TYPE backup_gateway_lifecycle_operation_duration_seconds counter");
            foreach (LifecycleMetricSnapshot metric in lifecycleMetrics.Snapshot())
            {
                string labels = $"target=\"{metric.TargetId}\",operation=\"{metric.Operation}\",outcome=\"{metric.Outcome}\"";
                output.Append("backup_gateway_lifecycle_operation_total{").Append(labels).Append("} ")
                    .AppendLine(metric.Count.ToString(CultureInfo.InvariantCulture));
                output.Append("backup_gateway_lifecycle_operation_duration_seconds{").Append(labels).Append("} ")
                    .AppendLine(metric.DurationSeconds.ToString("R", CultureInfo.InvariantCulture));
            }

            return new ContentResult
            {
                Content = output.ToString(),
                ContentType = "text/plain; version=0.0.4; charset=utf-8",
                StatusCode = StatusCodes.Status200OK,
            };
        }, cancellationToken);

    private static string GetMetricStateName(TargetLifecycleState state) => state switch
    {
        TargetLifecycleState.Unknown => "unknown",
        TargetLifecycleState.Offline => "offline",
        TargetLifecycleState.Starting => "starting",
        TargetLifecycleState.Online => "online",
        TargetLifecycleState.Stopping => "stopping",
        TargetLifecycleState.Faulted => "faulted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown target lifecycle state."),
    };

    private static void AppendMetric(StringBuilder output, string metric, string targetId, string freshness, int value) =>
        output.Append(metric)
            .Append("{target=\"")
            .Append(targetId)
            .Append("\",freshness=\"")
            .Append(freshness)
            .Append("\"} ")
            .AppendLine(value.ToString(CultureInfo.InvariantCulture));
}
