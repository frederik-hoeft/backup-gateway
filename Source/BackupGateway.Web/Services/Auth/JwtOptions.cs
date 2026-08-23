namespace BackupGateway.Web.Services.Auth;

internal sealed class JwtOptions
{
    public const string SECTION_NAME = "Auth:Jwt";

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required string RsaPrivateKeyFile { get; init; }

    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        JwtOptions options = configuration.GetSection(SECTION_NAME).Get<JwtOptions>()
            ?? throw new InvalidOperationException($"{SECTION_NAME} configuration is required.");
        options.Validate();
        return options;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || Issuer.Length > 256)
        {
            throw new InvalidOperationException($"{SECTION_NAME}:Issuer must contain between 1 and 256 characters.");
        }
        if (string.IsNullOrWhiteSpace(Audience) || Audience.Length > 256)
        {
            throw new InvalidOperationException($"{SECTION_NAME}:Audience must contain between 1 and 256 characters.");
        }
        if (string.IsNullOrWhiteSpace(RsaPrivateKeyFile))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:RsaPrivateKeyFile is required.");
        }
        if (TokenLifetime < TimeSpan.FromMinutes(1) || TokenLifetime > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:TokenLifetime must be between one minute and one hour.");
        }
        if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:ClockSkew must be between zero and five minutes.");
        }
    }
}
