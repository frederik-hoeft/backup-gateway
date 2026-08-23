using BackupGateway.Web.Data;
using BackupGateway.Web.Data.Model;
using BackupGateway.Web.Services.Observability;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text;

namespace BackupGateway.Web.Services.Auth;

internal sealed partial class IdentityBootstrapService(
    BackupGatewayDbContext dbContext,
    UserManager<IdentityUser<Guid>> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IConfiguration configuration,
    IAuditEventFactory auditEventFactory,
    ILogger<IdentityBootstrapService> logger)
{
    private const int MAX_BOOTSTRAP_CREDENTIAL_LENGTH = 1024;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRoleAsync(AuthRoles.ADMINISTRATOR);
        await EnsureRoleAsync(AuthRoles.BACKUP_CLIENT);

        if (await dbContext.Users.AsNoTracking().AnyAsync(cancellationToken))
        {
            IList<IdentityUser<Guid>> administrators = await userManager.GetUsersInRoleAsync(AuthRoles.ADMINISTRATOR);
            if (administrators.Count == 0)
            {
                throw new InvalidOperationException(
                    "Identity contains users but no administrator. Automatic bootstrap is intentionally disabled for non-empty stores.");
            }
            return;
        }

        BootstrapAdministratorOptions options = BootstrapAdministratorOptions.FromConfiguration(configuration);
        string credential = await ReadBootstrapCredentialAsync(options.CredentialFile, cancellationToken);

        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await dbContext.Users.AsNoTracking().AnyAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        IdentityUser<Guid> administrator = new()
        {
            Id = Guid.CreateVersion7(),
            UserName = options.Username,
        };
        IdentityResult createResult = await userManager.CreateAsync(administrator, credential);
        ThrowIfIdentityOperationFailed(createResult, "create bootstrap administrator");

        IdentityResult roleResult = await userManager.AddToRoleAsync(administrator, AuthRoles.ADMINISTRATOR);
        ThrowIfIdentityOperationFailed(roleResult, "assign bootstrap administrator role");

        dbContext.Add(auditEventFactory.Create(
            "security.bootstrap-administrator",
            "success",
            subjectClientId: administrator.Id));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogAdministratorBootstrapped(logger, administrator.Id);
    }

    [LoggerMessage(LogLevel.Information, "Bootstrapped the initial gateway administrator identity {AdministratorId}.")]
    private static partial void LogAdministratorBootstrapped(ILogger logger, Guid administratorId);

    private async Task EnsureRoleAsync(string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        IdentityRole<Guid> role = new(roleName) { Id = Guid.CreateVersion7() };
        IdentityResult result = await roleManager.CreateAsync(role);
        ThrowIfIdentityOperationFailed(result, $"create required role '{roleName}'");
    }

    private static async Task<string> ReadBootstrapCredentialAsync(string credentialFile, CancellationToken cancellationToken)
    {
        FileInfo file = new(credentialFile);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Bootstrap administrator credential file was not found.", file.FullName);
        }

        string credential = await File.ReadAllTextAsync(file.FullName, Encoding.UTF8, cancellationToken);
        credential = credential.TrimEnd('\r', '\n');
        if (credential.Length is < 24 or > MAX_BOOTSTRAP_CREDENTIAL_LENGTH)
        {
            throw new InvalidOperationException(
                $"Bootstrap administrator credential must contain between 24 and {MAX_BOOTSTRAP_CREDENTIAL_LENGTH} characters.");
        }
        return credential;
    }

    private static void ThrowIfIdentityOperationFailed(IdentityResult result, string operation)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Unable to {operation}.");
        }
    }

    private sealed class BootstrapAdministratorOptions
    {
        private const string SECTION_NAME = "Auth:BootstrapAdministrator";

        public required string Username { get; init; }

        public required string CredentialFile { get; init; }

        public static BootstrapAdministratorOptions FromConfiguration(IConfiguration configuration)
        {
            BootstrapAdministratorOptions options = configuration.GetSection(SECTION_NAME).Get<BootstrapAdministratorOptions>()
                ?? throw new InvalidOperationException(
                    $"{SECTION_NAME} configuration is required while the Identity store is empty.");
            if (string.IsNullOrWhiteSpace(options.Username) || options.Username.Length > 128)
            {
                throw new InvalidOperationException($"{SECTION_NAME}:Username must contain between 1 and 128 characters.");
            }
            if (string.IsNullOrWhiteSpace(options.CredentialFile))
            {
                throw new InvalidOperationException($"{SECTION_NAME}:CredentialFile is required.");
            }
            return options;
        }
    }
}
