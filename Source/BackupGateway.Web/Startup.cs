using BackupGateway.Web.Data;
using BackupGateway.Web.Services.Auth;
using BackupGateway.Web.Services.Hosting;
using BackupGateway.Web.Services.Leases;
using BackupGateway.Web.Services.Lifecycle;
using BackupGateway.Web.Services.Lifecycle.Transports;
using BackupGateway.Web.Services.Observability;
using BackupGateway.Web.Services.Targets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
        JwtOptions jwtOptions = JwtOptions.FromConfiguration(configuration);
        LeaseOptions leaseOptions = LeaseOptions.FromConfiguration(configuration);
        LifecycleOptions lifecycleOptions = LifecycleOptions.FromConfiguration(configuration);
        TargetCatalog targetCatalog = TargetCatalog.FromConfiguration(configuration);

        services.AddSingleton<IModelLoader, BackupGatewayModelLoader>();
        services.AddDbContext<BackupGatewayDbContext>(options => options.UseNpgsql(databaseConnection));

        services.AddIdentityCore<IdentityUser<Guid>>(options =>
            {
                options.Password.RequiredLength = 24;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<BackupGatewayDbContext>();

        services.AddSingleton(jwtOptions);
        services.AddSingleton(leaseOptions);
        services.AddSingleton(lifecycleOptions);
        services.AddSingleton<ITargetCatalog>(targetCatalog);
        services.AddSingleton<SingleInstanceGuard>();
        services.AddHostedService<SingleInstanceGuardMonitor>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CorrelationContext>();
        services.AddScoped<IAuditEventFactory, AuditEventFactory>();
        services.AddScoped<ILifecycleAuditWriter, LifecycleAuditWriter>();
        services.AddSingleton<LifecycleMetrics>();
        services.AddSingleton<IJwtSigningKeyProvider, RsaPemJwtSigningKeyProvider>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ServiceCredentialGenerator>();
        services.AddSingleton<InvalidCredentialTimingService>();
        services.AddScoped<GatewayJwtBearerEvents>();
        services.AddScoped<IdentityBootstrapService>();
        services.AddScoped<TargetConfigurationReconciler>();
        services.AddScoped<ITargetAuthorizationService, TargetAuthorizationService>();
        services.AddScoped<IAuthorizationHandler, TargetGrantAuthorizationHandler>();
        services.AddSingleton<TargetLeaseMutationSerializer>();
        services.AddScoped<LeaseService>();
        services.AddScoped<TargetDesiredStateService>();
        services.AddScoped<ITargetDesiredStateProvider>(provider => provider.GetRequiredService<TargetDesiredStateService>());
        services.AddScoped<ITargetRuntimeStateStore, TargetRuntimeStateStore>();
        services.AddSingleton<IWakeOnLanTransport, WakeOnLanTransport>();
        services.AddSingleton<ITargetReadinessProbe, TcpTargetReadinessProbe>();
        services.AddSingleton<IExternalProcessRunner, ExternalProcessRunner>();
        services.AddSingleton<ITargetShutdownTransport, SshShutdownTransport>();
        services.AddScoped<ITargetLifecycleReconciler, TargetLifecycleReconciler>();
        services.AddSingleton<TargetReconciliationCoordinator>();
        services.AddSingleton<TargetReconciliationQueue>();
        services.AddSingleton<ITargetReconciliationScheduler>(provider => provider.GetRequiredService<TargetReconciliationQueue>());
        services.AddHostedService(provider => provider.GetRequiredService<TargetReconciliationQueue>());
        services.AddHostedService<PeriodicTargetReconciliationService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IJwtSigningKeyProvider>((options, signingKeyProvider) =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.EventsType = typeof(GatewayJwtBearerEvents);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKeyProvider.ValidationKey,
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = jwtOptions.ClockSkew,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.ADMINISTRATOR, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthRoles.ADMINISTRATOR))
            .AddPolicy(AuthPolicies.TARGET_ACCESS, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthRoles.BACKUP_CLIENT)
                .AddRequirements(new TargetGrantRequirement()));

        services.AddTransactionManagement<BackupGatewayDbContext>(transactionOptions => transactionOptions
            .UseIsolationLevel(IsolationLevel.ReadCommitted));

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            CorrelationContext? correlationContext = context.HttpContext.RequestServices.GetService<CorrelationContext>();
            if (correlationContext is not null)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationContext.Id;
            }
        });
        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);

        return ValueTask.CompletedTask;
    }

    public static async ValueTask ConfigureAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        SingleInstanceGuard singleInstanceGuard = app.Services.GetRequiredService<SingleInstanceGuard>();
        await singleInstanceGuard.AcquireAsync(cancellationToken);

        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
            await context.Database.MigrateAsync(cancellationToken);

            _ = scope.ServiceProvider.GetRequiredService<IJwtSigningKeyProvider>();
            TargetConfigurationReconciler targetConfigurationReconciler =
                scope.ServiceProvider.GetRequiredService<TargetConfigurationReconciler>();
            await targetConfigurationReconciler.ReconcileAsync(cancellationToken);

            IdentityBootstrapService bootstrapService = scope.ServiceProvider.GetRequiredService<IdentityBootstrapService>();
            await bootstrapService.InitializeAsync(cancellationToken);
        }

        app.UseMiddleware<CorrelationMiddleware>();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static _ => false,
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("ready"),
        });
    }
}
