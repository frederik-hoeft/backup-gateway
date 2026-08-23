using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace BackupGateway.Web.Services.Auth;

public sealed class ServiceCredentialGenerator
{
    private const int CREDENTIAL_BYTES = 32;

    public string Generate() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(CREDENTIAL_BYTES));
}
