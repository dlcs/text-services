using Microsoft.Extensions.Configuration;

namespace TextServices.Infrastructure.Http.IpBlocking;

/// <summary>
/// Configuration for which IP addresses outbound content fetches may connect to.
/// </summary>
public class IpBlockingSettings
{
    public const string SectionName = "TextServices:IpBlocking";

    /// <summary>
    /// Additional IP ranges, in CIDR notation, that a fetched host is forbidden from resolving to.
    /// Loopback, link-local, unique-local and unspecified ranges are default blocked, regardless of this value.
    /// </summary>
    public string[] BlockedIpRanges { get; set; } = [];

    /// <summary>
    /// IP ranges, in CIDR notation, that a fetched host is permitted to resolve to even if otherwise blocked.
    /// Intended for local development, and deployments whose content legitimately sits on internal addresses.
    /// </summary>
    /// <remarks>
    /// An allowed range wins over <see cref="BlockedIpRanges"/> and over the default blocked ranges, so setting
    /// this widely (eg "0.0.0.0/0") disables the protection. The cloud instance-metadata addresses can never be
    /// allowed.
    /// </remarks>
    public string[] AllowedIpRanges { get; set; } = [];

    /// <summary>
    /// Bind settings from configuration, falling back to defaults if the section is absent.
    /// </summary>
    public static IpBlockingSettings FromConfiguration(IConfiguration configuration)
        => configuration.GetSection(SectionName).Get<IpBlockingSettings>() ?? new IpBlockingSettings();
}
