using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace BackupGateway.Web.Services.Auth;

internal sealed class RsaPemJwtSigningKeyProvider : IJwtSigningKeyProvider, IDisposable
{
    private readonly RSA _rsa;

    public RsaPemJwtSigningKeyProvider(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        FileInfo keyFile = new(options.RsaPrivateKeyFile);
        if (!keyFile.Exists)
        {
            throw new FileNotFoundException("JWT RSA private key file was not found.", keyFile.FullName);
        }

        string pem = File.ReadAllText(keyFile.FullName, Encoding.UTF8);
        _rsa = RSA.Create();
        try
        {
            _rsa.ImportFromPem(pem);
            _ = _rsa.ExportParameters(includePrivateParameters: true);
            if (_rsa.KeySize < 2048)
            {
                throw new InvalidOperationException("JWT RSA private key must be at least 2048 bits.");
            }

            byte[] publicKey = _rsa.ExportSubjectPublicKeyInfo();
            string keyId = Base64UrlEncoder.Encode(SHA256.HashData(publicKey));
            RsaSecurityKey securityKey = new(_rsa) { KeyId = keyId };
            ValidationKey = securityKey;
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
        }
        catch
        {
            _rsa.Dispose();
            throw;
        }
    }

    public SecurityKey ValidationKey { get; }

    public SigningCredentials SigningCredentials { get; }

    public void Dispose() => _rsa.Dispose();
}
