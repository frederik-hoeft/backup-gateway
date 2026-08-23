using BackupGateway.Web.Api.V1.Models.Administration;
using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Auth;
using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Observability;
using BackupGateway.Web.Services.Targets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Abstractions.Controllers;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AuthPolicies.ADMINISTRATOR)]
public sealed class AdministrationController(
    UserManager<IdentityUser<Guid>> userManager,
    ServiceCredentialGenerator credentialGenerator,
    ITargetCatalog targetCatalog,
    LeaseService leaseService,
    IAuditEventFactory auditEventFactory,
    ITransactionServiceHandle transactionService)
    : DatabaseController<BackupGatewayDbContext>(transactionService)
{
    [HttpPost("clients")]
    public Task<IActionResult> CreateClientAsync(
        [FromBody] CreateClientRequest request,
        CancellationToken cancellationToken) => Transaction.Scoped.RunAsync<IActionResult>(async (dbContext, transaction, ct) =>
    {
        if (!ClientIdentity.TryGetId(User, out Guid administratorId))
        {
            return transaction.Rollback(Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(request.Username) || !string.Equals(request.Username, request.Username.Trim(), StringComparison.Ordinal))
        {
            return transaction.Rollback(BadRequest());
        }

        string credential = credentialGenerator.Generate();
        IdentityUser<Guid> client = new()
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Username,
        };
        IdentityResult createResult = await userManager.CreateAsync(client, credential);
        if (!createResult.Succeeded)
        {
            return transaction.Rollback(Conflict());
        }

        IdentityResult roleResult = await userManager.AddToRoleAsync(client, AuthRoles.BACKUP_CLIENT);
        if (!roleResult.Succeeded)
        {
            return transaction.Rollback(StatusCode(StatusCodes.Status500InternalServerError));
        }

        dbContext.Add(CreateAuditEvent(
            administratorId,
            client.Id,
            "security.client-created"));
        await dbContext.SaveChangesAsync(ct);

        ClientCredentialResponse response = new(client.Id, client.UserName!, credential);
        return transaction.Commit(Created($"/api/v1/admin/clients/{client.Id}", response));
    }, cancellationToken);

    [HttpPost("clients/{clientId:guid}/credential")]
    public Task<IActionResult> RotateClientCredentialAsync(
        [FromRoute] Guid clientId,
        CancellationToken cancellationToken) => Transaction.Scoped.RunAsync<IActionResult>(async (dbContext, transaction, ct) =>
    {
        if (!ClientIdentity.TryGetId(User, out Guid administratorId))
        {
            return transaction.Rollback(Unauthorized());
        }

        IdentityUser<Guid>? client = await userManager.FindByIdAsync(clientId.ToString());
        if (client is null || !await userManager.IsInRoleAsync(client, AuthRoles.BACKUP_CLIENT))
        {
            return transaction.Rollback(NotFound());
        }

        string credential = credentialGenerator.Generate();
        if (await userManager.HasPasswordAsync(client))
        {
            IdentityResult removeResult = await userManager.RemovePasswordAsync(client);
            if (!removeResult.Succeeded)
            {
                return transaction.Rollback(StatusCode(StatusCodes.Status500InternalServerError));
            }
        }

        IdentityResult addResult = await userManager.AddPasswordAsync(client, credential);
        if (!addResult.Succeeded)
        {
            return transaction.Rollback(StatusCode(StatusCodes.Status500InternalServerError));
        }

        IdentityResult securityStampResult = await userManager.UpdateSecurityStampAsync(client);
        if (!securityStampResult.Succeeded)
        {
            return transaction.Rollback(StatusCode(StatusCodes.Status500InternalServerError));
        }
        await userManager.ResetAccessFailedCountAsync(client);
        await userManager.SetLockoutEndDateAsync(client, null);

        dbContext.Add(CreateAuditEvent(
            administratorId,
            client.Id,
            "security.client-credential-rotated"));
        await dbContext.SaveChangesAsync(ct);

        ClientCredentialResponse response = new(client.Id, client.UserName!, credential);
        return transaction.Commit(Ok(response));
    }, cancellationToken);

    [HttpPut("clients/{clientId:guid}/grants/{targetId}")]
    public Task<IActionResult> GrantTargetAsync(
        [FromRoute] Guid clientId,
        [FromRoute] string targetId,
        CancellationToken cancellationToken) => Transaction.Scoped.RunAsync<IActionResult>(async (dbContext, transaction, ct) =>
    {
        if (!ClientIdentity.TryGetId(User, out Guid administratorId))
        {
            return transaction.Rollback(Unauthorized());
        }
        if (!targetCatalog.TryGet(targetId, out _))
        {
            return transaction.Rollback(NotFound());
        }

        IdentityUser<Guid>? client = await userManager.FindByIdAsync(clientId.ToString());
        if (client is null || !await userManager.IsInRoleAsync(client, AuthRoles.BACKUP_CLIENT))
        {
            return transaction.Rollback(NotFound());
        }

        bool exists = await dbContext.Set<TargetGrant>().AsNoTracking()
            .AnyAsync(grant => grant.ClientId == clientId && grant.TargetId == targetId, ct);
        if (exists)
        {
            return transaction.Commit(NoContent());
        }

        dbContext.Add(new TargetGrant { ClientId = clientId, TargetId = targetId });
        dbContext.Add(CreateAuditEvent(
            administratorId,
            clientId,
            "security.target-granted",
            targetId));
        await dbContext.SaveChangesAsync(ct);
        return transaction.Commit(NoContent());
    }, cancellationToken);

    [HttpPost("targets/{targetId}/leases/{leaseId:guid}/force-release")]
    public async Task<IActionResult> ForceReleaseLeaseAsync(
        [FromRoute] string targetId,
        [FromRoute] Guid leaseId,
        CancellationToken cancellationToken)
    {
        if (!ClientIdentity.TryGetId(User, out Guid administratorId))
        {
            return Unauthorized();
        }

        LeaseReleaseResult result = await leaseService.ForceReleaseAsync(administratorId, targetId, leaseId, cancellationToken);
        return result.IsNotFound ? NotFound() : NoContent();
    }

    [HttpDelete("clients/{clientId:guid}/grants/{targetId}")]
    public Task<IActionResult> RevokeTargetAsync(
        [FromRoute] Guid clientId,
        [FromRoute] string targetId,
        CancellationToken cancellationToken) => Transaction.Scoped.RunAsync<IActionResult>(async (dbContext, transaction, ct) =>
    {
        if (!ClientIdentity.TryGetId(User, out Guid administratorId))
        {
            return transaction.Rollback(Unauthorized());
        }
        if (!targetCatalog.TryGet(targetId, out _))
        {
            return transaction.Rollback(NotFound());
        }

        TargetGrant? grant = await dbContext.Set<TargetGrant>()
            .SingleOrDefaultAsync(candidate => candidate.ClientId == clientId && candidate.TargetId == targetId, ct);
        if (grant is null)
        {
            return transaction.Commit(NoContent());
        }

        dbContext.Remove(grant);
        dbContext.Add(CreateAuditEvent(
            administratorId,
            clientId,
            "security.target-revoked",
            targetId));
        await dbContext.SaveChangesAsync(ct);
        return transaction.Commit(NoContent());
    }, cancellationToken);

    private AuditEvent CreateAuditEvent(
        Guid administratorId,
        Guid subjectClientId,
        string eventType,
        string? targetId = null) => auditEventFactory.Create(
            eventType,
            "success",
            actorClientId: administratorId,
            subjectClientId: subjectClientId,
            targetId: targetId);

}
