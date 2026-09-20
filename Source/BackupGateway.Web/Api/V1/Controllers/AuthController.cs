using BackupGateway.Web.Api.V1.Models.Auth;
using BackupGateway.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using Microsoft.EntityFrameworkCore;
using Wkg.AspNetCore.Abstractions.Controllers;
using BackupGateway.Web.Data;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed partial class AuthController
(
    UserManager<IdentityUser<Guid>> userManager,
    SignInManager<IdentityUser<Guid>> signInManager,
    IJwtTokenService jwtTokenService,
    InvalidCredentialTimingService invalidCredentialTimingService,
    ILogger<AuthController> logger,
    ITransactionServiceHandle transactionHandle
) : DatabaseController<BackupGatewayDbContext>(transactionHandle)
{
    [AllowAnonymous]
    [HttpPost("token")]
    public Task<ActionResult<TokenResponse>> IssueTokenAsync([FromBody] TokenRequest request, CancellationToken cancellationToken) =>
        Transaction.Scoped.RunReadOnlyAsync<ActionResult<TokenResponse>>(async (dbContext, ct) =>
    {
        string normalizedUsername = userManager.NormalizeName(request.Username)
        ?? throw new InvalidOperationException("Unable to normalize service identity username.");
        IdentityUser<Guid>? user = await userManager.Users
            .SingleOrDefaultAsync(candidate => candidate.NormalizedUserName == normalizedUsername, cancellationToken);
        if (user is null)
        {
            invalidCredentialTimingService.Consume(request.Credential);
            LogAuthenticationFailure(logger);
            return Unauthorized();
        }

        SignInResult signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Credential, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            if (signInResult.IsLockedOut)
            {
                invalidCredentialTimingService.Consume(request.Credential);
            }
            LogAuthenticationFailure(logger);
            return Unauthorized();
        }

        IList<string> roles = await userManager.GetRolesAsync(user);
        AccessToken token = jwtTokenService.Issue(user, roles);
        return Ok(new TokenResponse(token.Value, "Bearer", token.ExpiresAtUtc));
    }, cancellationToken);

    [LoggerMessage(LogLevel.Warning, "Service identity authentication failed.")]
    private static partial void LogAuthenticationFailure(ILogger logger);
}
