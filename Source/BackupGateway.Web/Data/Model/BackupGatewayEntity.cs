using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wkg.EntityFrameworkCore.Configuration;

namespace BackupGateway.Web.Data.Model;

/// <summary>
/// Base model for durable Backup Gateway domain entities.
/// </summary>
public abstract class BackupGatewayEntity : IDiscoverableBaseModelConfiguration<BackupGatewayEntity>
{
    /// <summary>
    /// Stable entity identifier. Domain identifiers are generated in-process so externally supplied lease IDs and
    /// internally generated IDs use the same non-database-generated mapping contract.
    /// </summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    static void IBaseModelConfiguration<BackupGatewayEntity>.ConfigureBaseModel<TChildClass>(EntityTypeBuilder<TChildClass> self)
    {
        ArgumentNullException.ThrowIfNull(self);

        self.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();
    }
}
