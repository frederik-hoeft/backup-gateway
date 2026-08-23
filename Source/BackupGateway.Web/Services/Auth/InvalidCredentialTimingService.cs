using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;

namespace BackupGateway.Web.Services.Auth;

public sealed class InvalidCredentialTimingService
{
    private readonly IdentityUser<Guid> _dummyUser = new()
    {
        Id = Guid.CreateVersion7(),
        UserName = "invalid-credential-timing",
    };
    private readonly PasswordHasher<IdentityUser<Guid>> _passwordHasher = new();
    private readonly string _dummyHash;

    public InvalidCredentialTimingService()
    {
        string dummyCredential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _dummyHash = _passwordHasher.HashPassword(_dummyUser, dummyCredential);
    }

    public void Consume(string suppliedCredential)
    {
        ArgumentNullException.ThrowIfNull(suppliedCredential);
        _ = _passwordHasher.VerifyHashedPassword(_dummyUser, _dummyHash, suppliedCredential);
    }
}
