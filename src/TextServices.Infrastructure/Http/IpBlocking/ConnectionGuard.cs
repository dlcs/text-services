using System.Net;
using System.Net.Sockets;

namespace TextServices.Infrastructure.Http.IpBlocking;

/// <summary>
/// Opens outgoing connections for content fetches, refusing any that <see cref="IIpAddressPolicy"/> blocks.
/// </summary>
/// <remarks>
/// Used as <see cref="SocketsHttpHandler.ConnectCallback"/> - this is called once per connection so every hop of
/// a redirect chain is verified, not just the URI the API consumer specified. Refusals aren't logged here; the
/// details are carried on <see cref="IpAddressBlockedException"/> and can be logged by the caller
/// </remarks>
public class ConnectionGuard(IIpAddressPolicy addressPolicy)
{
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await GetAddresses(endpoint.Host, cancellationToken);
        EnsureAllAddressesAllowed(endpoint.Host, addresses);

        // Connect to the addresses we verified, rather than the host, so that a second DNS lookup can't send us
        // somewhere else. TLS is handled above this callback so SNI + certificate validation still use the host.
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<IPAddress[]> GetAddresses(string host, CancellationToken cancellationToken)
        => IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

    /// <summary>
    /// Refuse the connection if any resolved address is blocked - a host that returns a mix of allowed and
    /// blocked addresses shouldn't be reachable by retrying until an allowed one is picked.
    /// </summary>
    private void EnsureAllAddressesAllowed(string host, IPAddress[] addresses)
    {
        foreach (var address in addresses)
        {
            var blockedBy = addressPolicy.GetBlockingRange(address);
            if (blockedBy != null) throw new IpAddressBlockedException(host, address, blockedBy.Value);
        }
    }
}
