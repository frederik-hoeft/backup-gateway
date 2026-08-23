using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackupGateway.Web.Data;

/// <summary>
/// Design-time DbContext factory used by EF Core migration tooling without requiring runtime secrets.
/// </summary>
internal sealed class BackupGatewayDbContextFactory : IDesignTimeDbContextFactory<BackupGatewayDbContext>
{
    public BackupGatewayDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        DbContextOptionsBuilder<BackupGatewayDbContext> options = new();
        options.UseNpgsql("Host=localhost;Database=backup_gateway_design_time");
        return new BackupGatewayDbContext(options.Options, new BackupGatewayModelLoader());
    }
}
