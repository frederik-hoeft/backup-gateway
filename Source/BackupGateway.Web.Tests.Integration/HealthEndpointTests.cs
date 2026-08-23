using BackupGateway.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class HealthEndpointTests
{
    [TestMethod]
    public async Task GetLiveness_ReturnsOkAsync()
    {
        await using WebApplicationFactory<BackupGatewayApplication> application = new();
        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
