using BackupGateway.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackupGateway.Web.Tests.Integration;

internal static class IntegrationTestDatabase
{
    private const string CONNECTION_ENVIRONMENT_VARIABLE = "BACKUP_GATEWAY_TEST_DATABASE";

    public static string RequireConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable(CONNECTION_ENVIRONMENT_VARIABLE);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive(
                $"Set {CONNECTION_ENVIRONMENT_VARIABLE} to a dedicated PostgreSQL test database connection string to run integration tests.");
        }
        return connectionString!;
    }

    public static async Task<ServiceProvider> CreateServiceProviderAsync(bool includeTarget = false)
    {
        ServiceCollection services = new();
        using ConfigurationManager configuration = new();
        configuration["ConnectionStrings:DatabaseConnection"] = RequireConnectionString();
        IntegrationTestSecurity.Apply(configuration);
        if (includeTarget)
        {
            IntegrationTestSecurity.ApplyTarget(configuration);
        }
        await Startup.ConfigureServicesAsync(services, configuration);
        return services.BuildServiceProvider();
    }

    public static async Task ResetAsync()
    {
        await using ServiceProvider provider = await CreateServiceProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();

        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }
}
