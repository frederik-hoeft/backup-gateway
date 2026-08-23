namespace BackupGateway.Web.Api.V1.Models.Diagnostics;

public sealed record AuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid CorrelationId,
    Guid? ActorClientId,
    Guid? SubjectClientId,
    string? TargetId,
    Guid? LeaseId,
    string EventType,
    string Outcome,
    string? Details);
