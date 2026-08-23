using Npgsql;
using System.Diagnostics.CodeAnalysis;

namespace BackupGateway.Web.Services.Hosting;

internal sealed partial class SingleInstanceGuard(
    IConfiguration configuration,
    ILogger<SingleInstanceGuard> logger) : IAsyncDisposable
{
    private const long AdvisoryLockId = 0x4261636B75704757;
    private NpgsqlConnection? _connection;

    public bool IsHeld => _connection is { State: System.Data.ConnectionState.Open };

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Successful acquisition transfers connection ownership to the guard, which disposes it.")]
    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            throw new InvalidOperationException("The single-instance guard has already been initialized.");
        }

        string connectionString = configuration.GetConnectionString("DatabaseConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DatabaseConnection configuration is required.");
        NpgsqlConnectionStringBuilder connectionOptions = new(connectionString)
        {
            Pooling = false,
            KeepAlive = 15,
        };
        NpgsqlConnection? connection = null;
        try
        {
            connection = new NpgsqlConnection(connectionOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@lock_id);";
            _ = command.Parameters.AddWithValue("lock_id", AdvisoryLockId);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is not true)
            {
                throw new InvalidOperationException(
                    "Another active Backup Gateway instance already holds the deployment lock.");
            }

            _connection = connection;
            connection = null;
            LogAcquired(logger);
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = _connection
            ?? throw new InvalidOperationException("The single-instance guard has not been acquired.");
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
    }

    [LoggerMessage(LogLevel.Information, "Acquired the PostgreSQL single-instance deployment lock.")]
    private static partial void LogAcquired(ILogger logger);
}
