using BackupGateway.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class HealthEndpointTests
{
    [TestMethod]
    public async Task GetLiveness_ReturnsOkAsync()
    {
        string connectionString = IntegrationTestDatabase.RequireConnectionString();
        await using WebApplicationFactory<BackupGatewayApplication> application =
            new WebApplicationFactory<BackupGatewayApplication>().WithWebHostBuilder(builder =>
                builder.UseSetting("ConnectionStrings:DatabaseConnection", connectionString));
        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
