using BackupGateway.Web.Data.Model;

namespace BackupGateway.Web.Services.Observability;

internal sealed class AuditEventFactory(CorrelationContext correlationContext, TimeProvider timeProvider) : IAuditEventFactory
{
    public AuditEvent Create(
        string eventType,
        string outcome,
        Guid? actorClientId = null,
        Guid? subjectClientId = null,
        string? targetId = null,
        Guid? leaseId = null,
        string? details = null) => new()
    {
        OccurredAtUtc = timeProvider.GetUtcNow(),
        CorrelationId = correlationContext.Id,
        ActorClientId = actorClientId,
        SubjectClientId = subjectClientId,
        TargetId = targetId,
        LeaseId = leaseId,
        EventType = eventType,
        Outcome = outcome,
        Details = details,
    };
}
