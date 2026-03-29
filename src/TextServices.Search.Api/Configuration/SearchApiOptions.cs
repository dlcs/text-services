namespace TextServices.Search.Api.Configuration;

public class SearchApiOptions
{
    /// <summary>
    /// Public base URL of this Search API (e.g. "https://search.example.org").
    /// Used to construct self-referencing IIIF URIs in responses.
    /// When empty, the URL is derived from the incoming HTTP request —
    /// configure this explicitly when running behind a reverse proxy.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Sliding expiration for cached Text and AutoComplete objects.</summary>
    public int CacheSlidingExpirationMinutes { get; set; } = 30;

    /// <summary>Root path of the filesystem text store (must match the Builder API's storage path).</summary>
    public string StorageRootPath { get; set; } = "textservices-data";
}
