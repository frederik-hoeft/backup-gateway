namespace BackupGateway.Web.Services.Observability;

internal sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    private const string HEADER_NAME = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlationContext)
    {
        string correlationId = correlationContext.Id.ToString();
        context.Response.Headers[HEADER_NAME] = correlationId;
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
        });
        await next(context);
    }
}
