using BackupGateway.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class HealthEndpointTests
{
    [TestMethod]
    public async Task GetLivenessReturnsOkAsync()
    {
        string connectionString = IntegrationTestDatabase.RequireConnectionString();
        await using WebApplicationFactory<BackupGatewayApplication> baseApplication = new();
        await using WebApplicationFactory<BackupGatewayApplication> application =
            baseApplication.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DatabaseConnection", connectionString);
                IntegrationTestSecurity.Apply(builder);
            });
        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
    [TestMethod]
    public async Task GetOpenApiContractReturnsEmbeddedV1DocumentAsync()
    {
        string connectionString = IntegrationTestDatabase.RequireConnectionString();
        await using WebApplicationFactory<BackupGatewayApplication> baseApplication = new();
        await using WebApplicationFactory<BackupGatewayApplication> application =
            baseApplication.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DatabaseConnection", connectionString);
                IntegrationTestSecurity.Apply(builder);
            });
        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.yaml");
        string content = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.StartsWith(content, "openapi: 3.1.0");
        StringAssert.Contains(content, "/api/v1/targets/{targetId}/leases/{leaseId}:");
    }
}
