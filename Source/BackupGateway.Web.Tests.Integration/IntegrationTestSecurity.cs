using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace BackupGateway.Web.Tests.Integration;

internal static class IntegrationTestSecurity
{
    public const string ADMINISTRATOR_USERNAME = "integration-admin";
    public const string ADMINISTRATOR_CREDENTIAL = "integration-bootstrap-credential-0123456789";

    private static readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "backup-gateway-integration-tests",
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static readonly string _jwtKeyFile = Path.Combine(_directory, "jwt-signing-key.pem");
    private static readonly string _bootstrapCredentialFile = Path.Combine(_directory, "bootstrap-admin-credential");

    static IntegrationTestSecurity()
    {
        Directory.CreateDirectory(_directory);
        using RSA rsa = RSA.Create(2048);
        File.WriteAllText(_jwtKeyFile, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(_bootstrapCredentialFile, ADMINISTRATOR_CREDENTIAL);
    }

    public static void Apply(IConfiguration configuration)
    {
        configuration["Auth:Jwt:Issuer"] = "backup-gateway-integration-tests";
        configuration["Auth:Jwt:Audience"] = "backup-gateway-integration-clients";
        configuration["Auth:Jwt:RsaPrivateKeyFile"] = _jwtKeyFile;
        configuration["Auth:BootstrapAdministrator:Username"] = ADMINISTRATOR_USERNAME;
        configuration["Auth:BootstrapAdministrator:CredentialFile"] = _bootstrapCredentialFile;
    }

    public static void Apply(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:Jwt:Issuer", "backup-gateway-integration-tests");
        builder.UseSetting("Auth:Jwt:Audience", "backup-gateway-integration-clients");
        builder.UseSetting("Auth:Jwt:RsaPrivateKeyFile", _jwtKeyFile);
        builder.UseSetting("Auth:BootstrapAdministrator:Username", ADMINISTRATOR_USERNAME);
        builder.UseSetting("Auth:BootstrapAdministrator:CredentialFile", _bootstrapCredentialFile);
    }
}
