using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;

namespace BackupGateway.Web.Services.Auth;

internal sealed class JwtTokenService(JwtOptions options, IJwtSigningKeyProvider signingKeyProvider) : IJwtTokenService
{
    internal const string SECURITY_STAMP_CLAIM = "backup_gateway_security_stamp";

    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public AccessToken Issue(IdentityUser<Guid> user, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        string username = user.UserName
            ?? throw new InvalidOperationException("Identity user must have a username before a token can be issued.");
        string securityStamp = user.SecurityStamp
            ?? throw new InvalidOperationException("Identity user must have a security stamp before a token can be issued.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.Add(options.TokenLifetime);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new(SECURITY_STAMP_CLAIM, securityStamp),
        ];
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        JwtSecurityToken token = new(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signingKeyProvider.SigningCredentials);
        return new AccessToken(_tokenHandler.WriteToken(token), expiresAt);
    }
}
