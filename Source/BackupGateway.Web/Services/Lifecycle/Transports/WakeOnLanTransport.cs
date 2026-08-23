using BackupGateway.Web.Services.Targets;
using System.Net;
using System.Net.Sockets;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal sealed class WakeOnLanTransport : IWakeOnLanTransport
{
    public async Task SendAsync(TargetDefinition target, CancellationToken cancellationToken)
    {
        byte[] packet = CreateMagicPacket(target.WakeOnLan.MacAddress.GetAddressBytes());
        IPEndPoint endpoint = new(target.WakeOnLan.Destination, target.WakeOnLan.Port);
        using UdpClient client = new(endpoint.AddressFamily)
        {
            EnableBroadcast = true,
        };
        _ = await client.SendAsync(packet, endpoint, cancellationToken);
    }

    internal static byte[] CreateMagicPacket(ReadOnlySpan<byte> macAddress)
    {
        if (macAddress.Length != 6)
        {
            throw new ArgumentException("Wake-on-LAN requires a six-byte MAC address.", nameof(macAddress));
        }

        byte[] packet = new byte[6 + 16 * macAddress.Length];
        packet.AsSpan(0, 6).Fill(0xff);
        for (int offset = 6; offset < packet.Length; offset += macAddress.Length)
        {
            macAddress.CopyTo(packet.AsSpan(offset, macAddress.Length));
        }
        return packet;
    }
}
