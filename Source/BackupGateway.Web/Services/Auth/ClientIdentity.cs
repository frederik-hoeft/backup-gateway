using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BackupGateway.Web.Services.Auth;

internal static class ClientIdentity
{
    public static bool TryGetId(ClaimsPrincipal principal, out Guid clientId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out clientId);
    }
}
