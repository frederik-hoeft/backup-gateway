using BackupGateway.Web.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;

namespace BackupGateway.Web.Services.Auth;

internal sealed class GatewayJwtBearerEvents(BackupGatewayDbContext dbContext) : JwtBearerEvents
{
    public async override Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        string? tokenSecurityStamp = context.Principal?.FindFirst(JwtTokenService.SECURITY_STAMP_CLAIM)?.Value;
        if (!Guid.TryParse(subject, out Guid clientId) || string.IsNullOrEmpty(tokenSecurityStamp))
        {
            context.Fail("Token identity claims are invalid.");
            return;
        }

        IdentityUser<Guid>? user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == clientId, context.HttpContext.RequestAborted);
        if (user is null || !string.Equals(user.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
        {
            context.Fail("Token identity is no longer valid.");
        }
    }
}
