using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wkg.EntityFrameworkCore.Configuration;

namespace BackupGateway.Web.Data.Model;

/// <summary>
/// Last durable lifecycle observation for a configured target.
/// </summary>
public sealed class TargetRuntimeObservation : BackupGatewayEntity, IDiscoverableModelConfiguration<TargetRuntimeObservation>
{
    public required string TargetId { get; set; }

    public TargetLifecycleState State { get; set; } = TargetLifecycleState.Unknown;

    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static void Configure(EntityTypeBuilder<TargetRuntimeObservation> self)
    {
        ArgumentNullException.ThrowIfNull(self);

        self.ToTable("target_runtime_observations").HasKey(observation => observation.Id);

        self.Property(observation => observation.TargetId)
            .HasColumnName("target_id")
            .HasColumnType("varchar")
            .HasMaxLength(128)
            .IsRequired();

        self.Property(observation => observation.State)
            .HasColumnName("state")
            .HasColumnType("integer")
            .IsRequired();

        self.Property(observation => observation.ObservedAtUtc)
            .HasColumnName("observed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        self.HasIndex(observation => observation.TargetId, "ux_target_runtime_observations_target")
            .IsUnique();
    }
}
