using BackupGateway.Web.Services.Targets;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal interface ITargetReadinessProbe
{
    Task<bool> ProbeAsync(TargetDefinition target, CancellationToken cancellationToken);
}
