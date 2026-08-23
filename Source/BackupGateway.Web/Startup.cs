using BackupGateway.Web.Data;
using BackupGateway.Web.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using System.Security.Claims;
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
        services.AddSingleton<IJwtSigningKeyProvider, RsaPemJwtSigningKeyProvider>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ServiceCredentialGenerator>();
        services.AddSingleton<InvalidCredentialTimingService>();
        services.AddScoped<GatewayJwtBearerEvents>();
        services.AddScoped<IdentityBootstrapService>();
        services.AddScoped<ITargetAuthorizationService, TargetAuthorizationService>();
        services.AddScoped<IAuthorizationHandler, TargetGrantAuthorizationHandler>();

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

        services.AddControllers();
        services.AddHealthChecks();

        return ValueTask.CompletedTask;
    }

    public static async ValueTask ConfigureAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            BackupGatewayDbContext context = scope.ServiceProvider.GetRequiredService<BackupGatewayDbContext>();
            await context.Database.MigrateAsync(cancellationToken);

            _ = scope.ServiceProvider.GetRequiredService<IJwtSigningKeyProvider>();
            IdentityBootstrapService bootstrapService = scope.ServiceProvider.GetRequiredService<IdentityBootstrapService>();
            await bootstrapService.InitializeAsync(cancellationToken);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health/live");
    }
}
