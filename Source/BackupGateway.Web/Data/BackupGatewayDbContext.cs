using BackupGateway.Web.Data.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Wkg.EntityFrameworkCore.Configuration;
using Wkg.EntityFrameworkCore.Configuration.Policies.Defaults.EntityNamingPolicies;
using Wkg.EntityFrameworkCore.Configuration.Policies.Defaults.InheritanceValidationPolicies;
using Wkg.EntityFrameworkCore.Configuration.Policies.Defaults.PropertyMappingPolicies;
using Wkg.EntityFrameworkCore.Extensions;

namespace BackupGateway.Web.Data;

/// <summary>
/// Primary durable store for Identity, authorization grants, leases, runtime observations, and audit history.
/// </summary>
public sealed class BackupGatewayDbContext(
    DbContextOptions<BackupGatewayDbContext> options,
    IModelLoader modelLoader)
    : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.LoadModels(modelLoader, modelOptions => modelOptions
            .ConfigurePolicies(policies => policies
                .AddPolicy<EntityNaming>(naming => naming.RequireExplicit())
                .AddPolicy<PropertyMapping>(mapping => mapping.RequireExplicit())
                .AddPolicy<EntityInheritanceValidation>(entity => entity.MustExtend<BackupGatewayEntity>())));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyAuditEvents();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyAuditEvents();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAppendOnlyAuditEvents()
    {
        foreach (EntityEntry<AuditEvent> entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit events are append-only and cannot be modified or deleted.");
            }
        }
    }
}
