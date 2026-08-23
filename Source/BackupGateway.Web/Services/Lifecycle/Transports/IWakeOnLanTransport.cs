using BackupGateway.Web.Services.Targets;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal interface IWakeOnLanTransport
{
    Task SendAsync(TargetDefinition target, CancellationToken cancellationToken);
}
