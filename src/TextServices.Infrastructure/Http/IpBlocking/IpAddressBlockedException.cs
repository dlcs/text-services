using System.Net;

namespace TextServices.Infrastructure.Http.IpBlocking;

/// <summary>
/// Raised when a connection is refused because the host resolves to an address blocked by
/// <see cref="IIpAddressPolicy"/>.
/// </summary>
public class IpAddressBlockedException(string host, IPAddress address, IPNetwork blockedBy)
    : Exception($"Host '{host}' resolves to an IP address that is not permitted")
{
    /// <summary>Host, as specified in the requested URI, that was refused.</summary>
    public string Host { get; } = host;

    /// <summary>Address that <see cref="Host"/> resolved to.</summary>
    public IPAddress Address { get; } = address;

    /// <summary>Range that <see cref="Address"/> falls within.</summary>
    public IPNetwork BlockedBy { get; } = blockedBy;

    /// <summary>
    /// Find an <see cref="IpAddressBlockedException"/> in the chain of inner exceptions, if present.
    /// HttpClient wraps anything raised from SocketsHttpHandler.ConnectCallback in an HttpRequestException.
    /// </summary>
    /// <returns>Located exception, or null if the chain contains none</returns>
    public static IpAddressBlockedException? FindInChain(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is IpAddressBlockedException blocked) return blocked;
        }

        return null;
    }
}
