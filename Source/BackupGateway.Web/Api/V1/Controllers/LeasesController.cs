using BackupGateway.Web.Api.V1.Models.Leases;
using BackupGateway.Web.Services.Auth;
using BackupGateway.Web.Services.Leases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("api/v1/targets/{targetId}/leases")]
[Authorize(Policy = AuthPolicies.TARGET_ACCESS)]
public sealed class LeasesController(LeaseService leaseService) : ControllerBase
{
    [HttpPut("{leaseId:guid}")]
    public async Task<IActionResult> AcquireAsync(
        [FromRoute] string targetId,
        [FromRoute] Guid leaseId,
        CancellationToken cancellationToken)
    {
        if (!ClientIdentity.TryGetId(User, out Guid clientId))
        {
            return Unauthorized();
        }

        LeaseAcquireResult result = await leaseService.AcquireAsync(clientId, targetId, leaseId, cancellationToken);
        if (result.IsConflict)
        {
            return Conflict();
        }

        LeaseResponse response = CreateResponse(result.Lease!);
        return result.WasCreated
            ? CreatedAtAction(nameof(GetAsync), new { targetId, leaseId }, response)
            : Ok(response);
    }

    [HttpGet("{leaseId:guid}")]
    public async Task<ActionResult<LeaseResponse>> GetAsync(
        [FromRoute] string targetId,
        [FromRoute] Guid leaseId,
        CancellationToken cancellationToken)
    {
        if (!ClientIdentity.TryGetId(User, out Guid clientId))
        {
            return Unauthorized();
        }

        LeaseSnapshot? lease = await leaseService.GetAsync(clientId, targetId, leaseId, cancellationToken);
        return lease is null ? NotFound() : Ok(CreateResponse(lease));
    }

    [HttpPost("{leaseId:guid}/heartbeat")]
    public async Task<IActionResult> HeartbeatAsync(
        [FromRoute] string targetId,
        [FromRoute] Guid leaseId,
        CancellationToken cancellationToken)
    {
        if (!ClientIdentity.TryGetId(User, out Guid clientId))
        {
            return Unauthorized();
        }

        LeaseHeartbeatResult result = await leaseService.HeartbeatAsync(clientId, targetId, leaseId, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }
        if (result.IsNotHeld)
        {
            return Conflict(CreateResponse(result.Lease!));
        }
        return Ok(CreateResponse(result.Lease!));
    }

    [HttpDelete("{leaseId:guid}")]
    public async Task<IActionResult> ReleaseAsync(
        [FromRoute] string targetId,
        [FromRoute] Guid leaseId,
        CancellationToken cancellationToken)
    {
        if (!ClientIdentity.TryGetId(User, out Guid clientId))
        {
            return Unauthorized();
        }

        LeaseReleaseResult result = await leaseService.ReleaseAsync(clientId, targetId, leaseId, cancellationToken);
        return result.IsNotFound ? NotFound() : NoContent();
    }

    private LeaseResponse CreateResponse(LeaseSnapshot lease) => new(
        lease.Id,
        lease.TargetId,
        lease.State,
        lease.CreatedAtUtc,
        lease.LastHeartbeatAtUtc,
        lease.ReleasedAtUtc,
        leaseService.IsStale(lease),
        lease.TargetState,
        lease.TargetObservedAtUtc);
}
