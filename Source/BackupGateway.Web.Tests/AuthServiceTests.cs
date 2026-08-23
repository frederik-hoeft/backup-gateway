using BackupGateway.Web.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod]
    public void ServiceCredentialGeneratorProducesHighEntropyUrlSafeCredential()
    {
        ServiceCredentialGenerator generator = new();

        string first = generator.Generate();
        string second = generator.Generate();

        Assert.AreNotEqual(first, second);
        Assert.AreEqual(43, first.Length);
        Assert.IsTrue(first.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
    }

    [TestMethod]
    public void JwtTokenServiceIssuesSignedShortLivedToken()
    {
        using RSA rsa = RSA.Create(2048);
        RsaSecurityKey securityKey = new(rsa);
        TestSigningKeyProvider signingKeyProvider = new(securityKey);
        JwtOptions options = new()
        {
            Issuer = "backup-gateway-tests",
            Audience = "backup-gateway-test-clients",
            RsaPrivateKeyFile = "unused",
            TokenLifetime = TimeSpan.FromMinutes(10),
        };
        JwtTokenService tokenService = new(options, signingKeyProvider);
        IdentityUser<Guid> user = new()
        {
            Id = Guid.CreateVersion7(),
            UserName = "client-a",
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        AccessToken accessToken = tokenService.Issue(user, [AuthRoles.BACKUP_CLIENT]);
        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken.Value);

        Assert.AreEqual(options.Issuer, token.Issuer);
        Assert.IsTrue(token.Audiences.Contains(options.Audience, StringComparer.Ordinal));
        Assert.AreEqual(SecurityAlgorithms.RsaSha256, token.Header.Alg);
        Assert.AreEqual(user.Id.ToString(), token.Subject);
        Assert.IsTrue(token.Claims.Any(claim =>
            claim.Type == JwtTokenService.SECURITY_STAMP_CLAIM
            && string.Equals(claim.Value, user.SecurityStamp, StringComparison.Ordinal)));
        Assert.IsTrue(accessToken.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(9));
        Assert.IsTrue(accessToken.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(11));
    }

    private sealed class TestSigningKeyProvider(SecurityKey securityKey) : IJwtSigningKeyProvider
    {
        public SecurityKey ValidationKey => securityKey;

        public SigningCredentials SigningCredentials { get; } = new(securityKey, SecurityAlgorithms.RsaSha256);
    }
}
