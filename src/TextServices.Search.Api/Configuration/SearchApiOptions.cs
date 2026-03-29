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

    /// <summary>
    /// Absolute expiration cap for cached objects (hours).
    /// Prevents popular texts from staying in the LOH indefinitely.
    /// </summary>
    public int CacheAbsoluteExpirationHours { get; set; } = 4;

    /// <summary>
    /// Maximum total word count to hold in the memory cache across all cached Text objects.
    /// Each Text entry is sized by its word count; AutoComplete entries count as
    /// <see cref="AutoCompleteCacheSize"/> words each.
    /// When the limit is reached, the least-recently-used entries are evicted first.
    /// Default: 2,000,000 words (~50–80 MB depending on text density).
    /// </summary>
    public long CacheMaxWords { get; set; } = 2_000_000;

    /// <summary>
    /// Nominal word-count size charged to the cache for each AutoComplete object.
    /// AutoComplete is far smaller than Text, but must participate in the same size budget.
    /// </summary>
    public int AutoCompleteCacheSize { get; set; } = 1_000;

    /// <summary>Root path of the filesystem text store (must match the Builder API's storage path).</summary>
    public string StorageRootPath { get; set; } = "textservices-data";
}
