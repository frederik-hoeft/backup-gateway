namespace BackupGateway.Web.Services.Leases;

public sealed class LeaseOptions
{
    public const string SECTION_NAME = "Leases";

    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(15);

    public static LeaseOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        LeaseOptions options = configuration.GetSection(SECTION_NAME).Get<LeaseOptions>() ?? new LeaseOptions();
        if (options.StaleAfter < TimeSpan.FromMinutes(1) || options.StaleAfter > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:StaleAfter must be between one minute and one day.");
        }
        return options;
    }
}
