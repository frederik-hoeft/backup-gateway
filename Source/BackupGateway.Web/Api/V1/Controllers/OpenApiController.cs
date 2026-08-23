using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace BackupGateway.Web.Api.V1.Controllers;

[ApiController]
[Route("openapi/v1.yaml")]
[AllowAnonymous]
public sealed class OpenApiController : ControllerBase
{
    private const string ResourceName = "BackupGateway.OpenApi.V1";

    [HttpGet]
    [Produces("application/yaml")]
    public IActionResult Get()
    {
        Stream stream = typeof(OpenApiController).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded OpenAPI v1 contract is missing.");
        return File(stream, "application/yaml; charset=utf-8");
    }
}
