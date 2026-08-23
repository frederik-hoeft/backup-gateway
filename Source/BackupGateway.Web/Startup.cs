using BackupGateway.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Wkg.AspNetCore.Configuration;
using Wkg.AspNetCore.Transactions.Configuration;
using Wkg.EntityFrameworkCore.Configuration;

namespace BackupGateway.Web;

internal sealed class Startup : IAsyncStartupScript
{
    public static ValueTask ConfigureServicesAsync(IServiceCollection services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        string databaseConnection = configuration.GetConnectionString("DatabaseConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DatabaseConnection configuration is required.");

        services.AddSingleton<IModelLoader, BackupGatewayModelLoader>();
        services.AddDbContext<BackupGatewayDbContext>(options => options.UseNpgsql(databaseConnection));

        services.AddIdentityCore<IdentityUser<Guid>>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<BackupGatewayDbContext>();

        services.AddTransactionManagement<BackupGatewayDbContext>(transactionOptions => transactionOptions
            .UseIsolationLevel(IsolationLevel.ReadCommitted));

        services.AddControllers();
        services.AddHealthChecks();

        return ValueTask.CompletedTask;
    }

    public static async ValueTask ConfigureAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        app.MapControllers();
        app.MapHealthChecks("/health/live");

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        await using BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
