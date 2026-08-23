using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wkg.EntityFrameworkCore.Configuration;

namespace BackupGateway.Web.Data.Model;

/// <summary>
/// Grants one authenticated client permission to operate one configured backup target.
/// </summary>
public sealed class TargetGrant : BackupGatewayEntity, IDiscoverableModelConfiguration<TargetGrant>
{
    public Guid ClientId { get; set; }

    public required string TargetId { get; set; }

    public IdentityUser<Guid> Client { get; set; } = null!;

    public static void Configure(EntityTypeBuilder<TargetGrant> self)
    {
        ArgumentNullException.ThrowIfNull(self);

        self.ToTable("target_grants").HasKey(grant => grant.Id);

        self.Property(grant => grant.ClientId)
            .HasColumnName("client_id")
            .HasColumnType("uuid")
            .IsRequired();

        self.Property(grant => grant.TargetId)
            .HasColumnName("target_id")
            .HasColumnType("varchar")
            .HasMaxLength(128)
            .IsRequired();

        self.HasIndex(grant => new { grant.ClientId, grant.TargetId }, "ux_target_grants_client_target")
            .IsUnique();

        self.HasOne(grant => grant.Client)
            .WithMany()
            .HasForeignKey(grant => grant.ClientId)
            .HasConstraintName("fk_target_grants_client_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
