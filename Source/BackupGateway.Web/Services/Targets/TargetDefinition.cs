using System.Net;
using System.Net.NetworkInformation;

namespace BackupGateway.Web.Services.Targets;

/// <summary>
/// Validated operational definition for one dedicated backup target.
/// </summary>
public sealed record TargetDefinition(
    string Id,
    string Host,
    WakeOnLanDefinition WakeOnLan,
    ReadinessDefinition Readiness,
    ShutdownDefinition Shutdown);

/// <summary>
/// Fixed Wake-on-LAN transport configuration.
/// </summary>
public sealed record WakeOnLanDefinition(PhysicalAddress MacAddress, IPAddress Destination, int Port);

/// <summary>
/// Bounded TCP readiness probe configuration.
/// </summary>
public sealed record ReadinessDefinition(int Port, TimeSpan ConnectTimeout, TimeSpan RetryInterval, TimeSpan OverallTimeout);

/// <summary>
/// Fixed authenticated shutdown configuration.
/// </summary>
public sealed record ShutdownDefinition(
    int Port,
    string Username,
    string Command,
    string PrivateKeyFile,
    string HostKeyFingerprint,
    TimeSpan ConnectTimeout,
    TimeSpan CommandTimeout,
    TimeSpan OfflineTimeout,
    TimeSpan RetryInterval);
