using BackupGateway.Web.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackupGateway.Web.Services.Observability;

internal sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory serviceScopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        BackupGatewayDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.", exception);
        }
    }
}
