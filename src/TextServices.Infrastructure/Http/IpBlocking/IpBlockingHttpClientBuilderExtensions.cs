using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace TextServices.Infrastructure.Http.IpBlocking;

public static class IpBlockingHttpClientBuilderExtensions
{
    /// <summary>
    /// Refuse connections from this client to any host that resolves to an address blocked by
    /// <see cref="IpAddressPolicy"/>, configured from <see cref="IpBlockingSettings.SectionName"/>.
    /// </summary>
    /// <exception cref="FormatException">Thrown if any configured range is not valid CIDR notation</exception>
    public static IHttpClientBuilder UseIpBlocking(this IHttpClientBuilder builder, IConfiguration configuration)
    {
        // Policy is constructed here, rather than by DI, so that an invalid range fails at startup; the factory is
        // only there to get hold of a logger
        var addressPolicy = new IpAddressPolicy(IpBlockingSettings.FromConfiguration(configuration));

        builder.Services.TryAddSingleton<IIpAddressPolicy>(provider => LogAllowedRanges(addressPolicy, provider));
        builder.Services.TryAddSingleton<ConnectionGuard>();

        return builder.ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
        {
            ConnectCallback = provider.GetRequiredService<ConnectionGuard>().ConnectAsync,

            // A proxy would resolve the requested host on our behalf, leaving ConnectionGuard to check the proxy
            // address only. Disabled so that an ambient HTTP_PROXY can't defeat the address checks
            UseProxy = false,
        });
    }

    /// <summary>
    /// Warn if any otherwise-blocked ranges have been opened up, as this weakens the protection.
    /// This is output on first resolution, not startup.
    /// </summary>
    private static IpAddressPolicy LogAllowedRanges(IpAddressPolicy addressPolicy, IServiceProvider provider)
    {
        if (addressPolicy.AllowedRanges.Count > 0)
        {
            provider.GetRequiredService<ILogger<IpAddressPolicy>>().LogWarning(
                "Content fetches are permitted to resolve to {AllowedIpRanges}, which would otherwise be blocked",
                addressPolicy.AllowedRanges);
        }

        return addressPolicy;
    }
}
