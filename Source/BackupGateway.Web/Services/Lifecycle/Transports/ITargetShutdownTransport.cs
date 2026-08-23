using BackupGateway.Web.Services.Targets;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal interface ITargetShutdownTransport
{
    Task RequestShutdownAsync(TargetDefinition target, CancellationToken cancellationToken);
}
