using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wkg.EntityFrameworkCore.Configuration;

namespace BackupGateway.Web.Data.Model;

/// <summary>
/// Durable claim that keeps a target available for one backup client.
/// </summary>
/// <remarks>
/// Client identity is intentionally stored as an immutable snapshot rather than a foreign key to ASP.NET Core Identity.
/// Revoking or deleting a client must not implicitly remove a held lease and thereby authorize target shutdown.
/// </remarks>
public sealed class BackupLease : BackupGatewayEntity, IDiscoverableModelConfiguration<BackupLease>
{
    public Guid ClientId { get; set; }

    public required string TargetId { get; set; }

    public BackupLeaseState State { get; set; } = BackupLeaseState.Held;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastHeartbeatAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReleasedAtUtc { get; set; }

    public static void Configure(EntityTypeBuilder<BackupLease> self)
    {
        ArgumentNullException.ThrowIfNull(self);

        self.ToTable("backup_leases", null, table =>
        {
            table.HasCheckConstraint(
                "ck_backup_leases_state",
                "state IN (1, 2, 3)");
            table.HasCheckConstraint(
                "ck_backup_leases_release_state",
                "(state = 1 AND released_at_utc IS NULL) OR (state IN (2, 3) AND released_at_utc IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_backup_leases_heartbeat_time",
                "last_heartbeat_at_utc >= created_at_utc");
        }).HasKey(lease => lease.Id);

        self.Property(lease => lease.ClientId)
            .HasColumnName("client_id")
            .HasColumnType("uuid")
            .IsRequired();

        self.Property(lease => lease.TargetId)
            .HasColumnName("target_id")
            .HasColumnType("varchar")
            .HasMaxLength(128)
            .IsRequired();

        self.Property(lease => lease.State)
            .HasColumnName("state")
            .HasColumnType("integer")
            .IsRequired();

        self.Property(lease => lease.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        self.Property(lease => lease.LastHeartbeatAtUtc)
            .HasColumnName("last_heartbeat_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        self.Property(lease => lease.ReleasedAtUtc)
            .HasColumnName("released_at_utc")
            .HasColumnType("timestamp with time zone");

        self.HasIndex(lease => new { lease.TargetId, lease.State }, "idx_backup_leases_target_state");
        self.HasIndex(lease => new { lease.ClientId, lease.TargetId }, "idx_backup_leases_client_target");
    }
}
