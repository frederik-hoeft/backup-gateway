using BackupGateway.Web.Data.Model;

namespace BackupGateway.Web.Services.Lifecycle;

internal interface ITargetRuntimeStateStore
{
    Task<TargetRuntimeSnapshot> GetAsync(string targetId, CancellationToken cancellationToken);

    Task SetAsync(string targetId, TargetLifecycleState state, CancellationToken cancellationToken);

    Task RecordFaultAsync(string targetId, string failureCode, CancellationToken cancellationToken);
}

internal sealed record TargetRuntimeSnapshot(TargetLifecycleState State, DateTimeOffset ObservedAtUtc);
