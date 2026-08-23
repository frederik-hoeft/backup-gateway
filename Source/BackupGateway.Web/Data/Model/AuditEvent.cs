using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wkg.EntityFrameworkCore.Configuration;

namespace BackupGateway.Web.Data.Model;

/// <summary>
/// Append-only durable security and lifecycle audit record.
/// </summary>
public sealed class AuditEvent : BackupGatewayEntity, IDiscoverableModelConfiguration<AuditEvent>
{
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Guid CorrelationId { get; set; }

    public Guid? ActorClientId { get; set; }

    public string? TargetId { get; set; }

    public Guid? LeaseId { get; set; }

    public required string EventType { get; set; }

    public required string Outcome { get; set; }

    public string? Details { get; set; }

    public static void Configure(EntityTypeBuilder<AuditEvent> self)
    {
        ArgumentNullException.ThrowIfNull(self);

        self.ToTable("audit_events").HasKey(auditEvent => auditEvent.Id);

        self.Property(auditEvent => auditEvent.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        self.Property(auditEvent => auditEvent.CorrelationId)
            .HasColumnName("correlation_id")
            .HasColumnType("uuid")
            .IsRequired();

        self.Property(auditEvent => auditEvent.ActorClientId)
            .HasColumnName("actor_client_id")
            .HasColumnType("uuid");

        self.Property(auditEvent => auditEvent.TargetId)
            .HasColumnName("target_id")
            .HasColumnType("varchar")
            .HasMaxLength(128);

        self.Property(auditEvent => auditEvent.LeaseId)
            .HasColumnName("lease_id")
            .HasColumnType("uuid");

        self.Property(auditEvent => auditEvent.EventType)
            .HasColumnName("event_type")
            .HasColumnType("varchar")
            .HasMaxLength(64)
            .IsRequired();

        self.Property(auditEvent => auditEvent.Outcome)
            .HasColumnName("outcome")
            .HasColumnType("varchar")
            .HasMaxLength(32)
            .IsRequired();

        self.Property(auditEvent => auditEvent.Details)
            .HasColumnName("details")
            .HasColumnType("varchar")
            .HasMaxLength(1024);

        self.HasIndex(auditEvent => auditEvent.OccurredAtUtc, "idx_audit_events_occurred_at");
        self.HasIndex(auditEvent => auditEvent.CorrelationId, "idx_audit_events_correlation_id");
        self.HasIndex(auditEvent => new { auditEvent.TargetId, auditEvent.OccurredAtUtc }, "idx_audit_events_target_time");
    }
}
