using Microsoft.IdentityModel.Tokens;

namespace BackupGateway.Web.Services.Auth;

internal interface IJwtSigningKeyProvider
{
    SecurityKey ValidationKey { get; }

    SigningCredentials SigningCredentials { get; }
}
