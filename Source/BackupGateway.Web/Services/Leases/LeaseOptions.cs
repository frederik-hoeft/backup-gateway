namespace BackupGateway.Web.Services.Leases;

public sealed class LeaseOptions
{
    public const string SectionName = "Leases";

    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(15);

    public static LeaseOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        LeaseOptions options = configuration.GetSection(SectionName).Get<LeaseOptions>() ?? new LeaseOptions();
        if (options.StaleAfter < TimeSpan.FromMinutes(1) || options.StaleAfter > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException($"{SectionName}:StaleAfter must be between one minute and one day.");
        }
        return options;
    }
}
