using BackupGateway.Web.Data.Model;

namespace BackupGateway.Web.Api.V1.Models.Leases;

public sealed record LeaseResponse(
    Guid LeaseId,
    string TargetId,
    BackupLeaseState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastHeartbeatAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    bool IsStale,
    TargetLifecycleState TargetState,
    DateTimeOffset? TargetObservedAtUtc);
