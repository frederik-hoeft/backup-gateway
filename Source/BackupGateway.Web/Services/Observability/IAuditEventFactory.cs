using BackupGateway.Web.Data.Model;

namespace BackupGateway.Web.Services.Observability;

public interface IAuditEventFactory
{
    AuditEvent Create(
        string eventType,
        string outcome,
        Guid? actorClientId = null,
        Guid? subjectClientId = null,
        string? targetId = null,
        Guid? leaseId = null,
        string? details = null);
}
