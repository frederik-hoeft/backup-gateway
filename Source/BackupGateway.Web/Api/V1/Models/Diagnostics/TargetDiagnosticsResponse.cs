using BackupGateway.Web.Data.Model;

namespace BackupGateway.Web.Api.V1.Models.Diagnostics;

public sealed record TargetDiagnosticsResponse(
    string TargetId,
    TargetLifecycleState State,
    DateTimeOffset? ObservedAtUtc,
    int HeldLeaseCount,
    int StaleLeaseCount,
    bool LeasesTruncated,
    IReadOnlyList<HeldLeaseDiagnostic> Leases);

public sealed record HeldLeaseDiagnostic(
    Guid LeaseId,
    Guid ClientId,
    DateTimeOffset LastHeartbeatAtUtc,
    bool IsStale);
