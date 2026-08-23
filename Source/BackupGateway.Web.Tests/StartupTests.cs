using BackupGateway.Web;
using BackupGateway.Web.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wkg.AspNetCore.Transactions;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class StartupTests
{
    [TestMethod]
    public async Task ConfigureServices_RegistersPersistenceInfrastructureAsync()
    {
        ServiceCollection services = new();
        using ConfigurationManager configuration = new();
        configuration["ConnectionStrings:DatabaseConnection"] =
            "Host=localhost;Port=5432;Database=backup_gateway_test;Username=backup_gateway;Password=test";
        configuration["Auth:Jwt:Issuer"] = "backup-gateway-tests";
        configuration["Auth:Jwt:Audience"] = "backup-gateway-test-clients";
        configuration["Auth:Jwt:RsaPrivateKeyFile"] = "unused-for-service-registration";

        await Startup.ConfigureServicesAsync(services, configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckService healthCheckService = provider.GetRequiredService<HealthCheckService>();
        ITransactionService<BackupGatewayDbContext> transactionService =
            provider.GetRequiredService<ITransactionService<BackupGatewayDbContext>>();

        Assert.IsNotNull(healthCheckService);
        Assert.IsNotNull(transactionService);
    }
}
