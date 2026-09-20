using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("openapi/v1.yaml")]
[AllowAnonymous]
public sealed class OpenApiController : ControllerBase
{
    private const string RESOURCE_NAME = "BackupGateway.OpenApi.V1";

    [HttpGet]
    [Produces("application/yaml")]
    public IActionResult Get()
    {
        Stream stream = typeof(OpenApiController).Assembly.GetManifestResourceStream(RESOURCE_NAME)
            ?? throw new InvalidOperationException("Embedded OpenAPI v1 contract is missing.");
        return File(stream, "application/yaml; charset=utf-8");
    }
}
