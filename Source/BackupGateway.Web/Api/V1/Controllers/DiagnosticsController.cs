using BackupGateway.Web.Api.V1.Models.Diagnostics;
using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Auth;
using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("api/v1/admin/diagnostics")]
[Authorize(Policy = AuthPolicies.ADMINISTRATOR)]
public sealed class DiagnosticsController(
    ITransactionService<BackupGatewayDbContext> transactionService,
    ITargetCatalog targetCatalog,
    LeaseOptions leaseOptions,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaximumLeaseDetails = 100;
    private const int MaximumAuditEvents = 100;

    [HttpGet("targets/{targetId}")]
    public Task<IActionResult> GetTargetAsync([FromRoute] string targetId, CancellationToken cancellationToken)
    {
        if (!targetCatalog.TryGet(targetId, out _))
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        return transactionService.Scoped.RunReadOnlyAsync<IActionResult>(async (dbContext, ct) =>
        {
            TargetRuntimeObservation? observation = await dbContext.Set<TargetRuntimeObservation>()
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TargetId == targetId, ct);
            List<BackupLease> leases = await dbContext.Set<BackupLease>()
                .AsNoTracking()
                .Where(lease => lease.TargetId == targetId && lease.State == BackupLeaseState.Held)
                .OrderBy(lease => lease.LastHeartbeatAtUtc)
                .ToListAsync(ct);

            DateTimeOffset now = timeProvider.GetUtcNow();
            IReadOnlyList<HeldLeaseDiagnostic> leaseDetails = [.. leases
                .Take(MaximumLeaseDetails)
                .Select(lease => new HeldLeaseDiagnostic(
                    lease.Id,
                    lease.ClientId,
                    lease.LastHeartbeatAtUtc,
                    now - lease.LastHeartbeatAtUtc > leaseOptions.StaleAfter))];
            TargetDiagnosticsResponse response = new(
                targetId,
                observation?.State ?? TargetLifecycleState.Unknown,
                observation?.ObservedAtUtc,
                leases.Count,
                leases.Count(lease => now - lease.LastHeartbeatAtUtc > leaseOptions.StaleAfter),
                leases.Count > MaximumLeaseDetails,
                leaseDetails);
            return Ok(response);
        }, cancellationToken);
    }

    [HttpGet("audit")]
    public Task<IActionResult> GetAuditAsync(
        [FromQuery] string? targetId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaximumAuditEvents || targetId is { Length: > 128 })
        {
            return Task.FromResult<IActionResult>(BadRequest());
        }

        return transactionService.Scoped.RunReadOnlyAsync<IActionResult>(async (dbContext, ct) =>
        {
            IQueryable<AuditEvent> query = dbContext.Set<AuditEvent>().AsNoTracking();
            if (!string.IsNullOrEmpty(targetId))
            {
                query = query.Where(auditEvent => auditEvent.TargetId == targetId);
            }

            List<AuditEventResponse> events = await query
                .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
                .Take(limit)
                .Select(auditEvent => new AuditEventResponse(
                    auditEvent.Id,
                    auditEvent.OccurredAtUtc,
                    auditEvent.CorrelationId,
                    auditEvent.ActorClientId,
                    auditEvent.SubjectClientId,
                    auditEvent.TargetId,
                    auditEvent.LeaseId,
                    auditEvent.EventType,
                    auditEvent.Outcome,
                    auditEvent.Details))
                .ToListAsync(ct);
            return Ok(events);
        }, cancellationToken);
    }
}
