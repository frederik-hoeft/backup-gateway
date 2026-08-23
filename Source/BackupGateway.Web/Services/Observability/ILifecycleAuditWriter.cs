namespace BackupGateway.Web.Services.Observability;

internal interface ILifecycleAuditWriter
{
    Task WriteAsync(
        string targetId,
        string eventType,
        string outcome,
        string? details,
        CancellationToken cancellationToken);
}
