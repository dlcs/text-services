using System.Xml.Linq;

namespace TextServices.Builder.Api.Services;

/// <summary>
/// Fetches an ALTO XML file from a URI and returns its root element.
/// </summary>
public interface IAltoFetcher
{
    /// <summary>
    /// Downloads and parses the ALTO XML at <paramref name="uri"/>.
    /// Returns <see langword="null"/> when the resource is not found (HTTP 404)
    /// or the response body is empty — both treated as a sparse page.
    /// Throws for other HTTP errors or parse failures.
    /// </summary>
    Task<XElement?> FetchAsync(string uri, CancellationToken ct = default);
}
