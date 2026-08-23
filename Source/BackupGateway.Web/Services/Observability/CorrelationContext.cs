namespace BackupGateway.Web.Services.Observability;

internal sealed class CorrelationContext
{
    public Guid Id { get; } = Guid.CreateVersion7();
}
