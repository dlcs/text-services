using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Extensions.Logging;
using TextServices.Infrastructure.Http;
using TextServices.Pdf;
using TextServices.Search.Api.Features.Pdf;

namespace TextServices.Search.Api.Configuration;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPdfServices(this IServiceCollection services)
    {
        services.AddSingleton<PdfBuilder>();
        services.AddSingleton<IPdfGenerationService, PdfGenerationService>();
        services.AddSingleton<IPdfGenerationQueue, PdfGenerationQueue>();
        services.AddHostedService<PdfGenerationBackgroundService>();
        services.AddHttpClient(PdfBuilder.HttpClientName)
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(60);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents.Search);
            });

        return services;
    }

    /// <summary>
    /// Configures host to use x-forwarded-proto to set httpContext.Request.Scheme
    /// "KnownNetworks" (CIDR ranges) and/or "KnownProxies" (individual IPs) configuration keys restrict which
    /// upstream sources are trusted. If neither is present, headers are accepted from all sources (with a warning).
    /// </summary>
    public static IServiceCollection ConfigureForwardedHeaders(this IServiceCollection services,
        IConfiguration configuration)
    {
        var knownNetworks = configuration.GetValue<string>("KnownNetworks");
        var knownProxies = configuration.GetValue<string>("KnownProxies");

        var logger = new SerilogLoggerFactory(Log.Logger).CreateLogger("ServiceCollection");

        return services.Configure<ForwardedHeadersOptions>(opts =>
        {
            opts.ForwardedHeaders = ForwardedHeaders.XForwardedProto;

            var networks = knownNetworks.SplitSeparatedString(",").ToList();
            var proxies = knownProxies.SplitSeparatedString(",").ToList();

            if (networks.Count == 0 && proxies.Count == 0)
            {
                logger.LogWarning("Forwarded header values accepted from all networks and proxies");
                opts.KnownIPNetworks.Clear();
                opts.KnownProxies.Clear();
            }
            else
            {
                if (networks.Count > 0)
                {
                    logger.LogInformation("Forwarded header values accepted from networks: {KnownNetworks}", knownNetworks);
                    foreach (var network in networks)
                    {
                        opts.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                    }
                }

                if (proxies.Count > 0)
                {
                    logger.LogInformation("Forwarded header values accepted from proxies: {KnownProxies}", knownProxies);
                    foreach (var proxy in proxies)
                    {
                        opts.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
                    }
                }
            }
        });
    }

    private static IEnumerable<string> SplitSeparatedString(this string? str, string separator)
        => str?.Trim().Split(separator, StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>();
}
