using BackupGateway.Web;
using BackupGateway.Web.Api.V1.Models.Administration;
using BackupGateway.Web.Api.V1.Models.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BackupGateway.Web.Tests.Integration;

[TestClass]
public sealed class AuthenticationTests
{
    [TestInitialize]
    public Task ResetDatabaseAsync() => IntegrationTestDatabase.ResetAsync();

    [TestMethod]
    public async Task BootstrapAdministratorCanAuthenticateAndProvisionClientAsync()
    {
        await using WebApplicationFactory<BackupGatewayApplication> baseApplication = new();
        await using WebApplicationFactory<BackupGatewayApplication> application = CreateApplication(baseApplication);
        using HttpClient client = application.CreateClient();

        TokenResponse administratorToken = await AuthenticateAsync(
            client,
            IntegrationTestSecurity.ADMINISTRATOR_USERNAME,
            IntegrationTestSecurity.ADMINISTRATOR_CREDENTIAL);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administratorToken.AccessToken);

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/clients",
            new CreateClientRequest { Username = "backup-client-a" });
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        ClientCredentialResponse createdClient = (await createResponse.Content.ReadFromJsonAsync<ClientCredentialResponse>())!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(createdClient.Credential));

        TokenResponse clientToken = await AuthenticateAsync(client, createdClient.Username, createdClient.Credential);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clientToken.AccessToken);

        using HttpResponseMessage forbiddenResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/clients",
            new CreateClientRequest { Username = "must-not-be-created" });
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [TestMethod]
    public async Task InvalidCredentialReturnsUnauthorizedAsync()
    {
        await using WebApplicationFactory<BackupGatewayApplication> baseApplication = new();
        await using WebApplicationFactory<BackupGatewayApplication> application = CreateApplication(baseApplication);
        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new TokenRequest
            {
                Username = "unknown-client",
                Credential = "definitely-not-a-valid-credential",
            });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<BackupGatewayApplication> CreateApplication(
        WebApplicationFactory<BackupGatewayApplication> baseApplication)
    {
        string connectionString = IntegrationTestDatabase.RequireConnectionString();
        return baseApplication.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DatabaseConnection", connectionString);
            IntegrationTestSecurity.Apply(builder);
        });
    }

    private static async Task<TokenResponse> AuthenticateAsync(HttpClient client, string username, string credential)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new TokenRequest { Username = username, Credential = credential });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }
}
