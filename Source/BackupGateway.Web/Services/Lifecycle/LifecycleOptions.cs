namespace BackupGateway.Web.Services.Lifecycle;

internal sealed class LifecycleOptions
{
    private const string SectionName = "Lifecycle";

    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromMinutes(1);

    public static LifecycleOptions FromConfiguration(IConfiguration configuration)
    {
        LifecycleOptions options = configuration.GetSection(SectionName).Get<LifecycleOptions>() ?? new LifecycleOptions();
        if (options.ReconciliationInterval < TimeSpan.FromSeconds(10)
            || options.ReconciliationInterval > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException(
                $"{SectionName}:ReconciliationInterval must be between ten seconds and one hour.");
        }
        return options;
    }
}
