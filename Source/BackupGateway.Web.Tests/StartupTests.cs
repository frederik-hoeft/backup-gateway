using BackupGateway.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class StartupTests
{
    [TestMethod]
    public async Task ConfigureServices_RegistersHealthChecksAsync()
    {
        ServiceCollection services = new();
        using ConfigurationManager configuration = new();

        await Startup.ConfigureServicesAsync(services, configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckService healthCheckService = provider.GetRequiredService<HealthCheckService>();
        Assert.IsNotNull(healthCheckService);
    }
}
