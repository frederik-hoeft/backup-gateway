using BackupGateway.Web.Services.Targets;
using System.Net.Sockets;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal sealed class TcpTargetReadinessProbe : ITargetReadinessProbe
{
    public async Task<bool> ProbeAsync(TargetDefinition target, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(target.Readiness.ConnectTimeout);
        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(target.Host, target.Readiness.Port, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
