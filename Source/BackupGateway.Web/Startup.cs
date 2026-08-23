using Wkg.AspNetCore.Configuration;

namespace BackupGateway.Web;

internal sealed class Startup : IAsyncStartupScript
{
    public static ValueTask ConfigureServicesAsync(IServiceCollection services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        services.AddControllers();
        services.AddHealthChecks();

        return ValueTask.CompletedTask;
    }

    public static ValueTask ConfigureAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        app.MapControllers();
        app.MapHealthChecks("/health/live");

        return ValueTask.CompletedTask;
    }
}
