namespace TextServices.Builder.Api.Configuration;

public class TextServicesOptions
{
    /// <summary>
    /// Base URL of the deployed Search API (e.g. "https://search.example.org").
    /// Used to compute the searchV1/autocompleteV1 URLs in the job status response.
    /// Leave empty if the Search API is not yet deployed.
    /// </summary>
    public string SearchApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of ALTO files fetched concurrently within a single job.
    /// The right value depends on the source:
    /// <list type="bullet">
    ///   <item>Third-party HTTP (Wellcome, Internet Archive, etc.): 4–8 for politeness.</item>
    ///   <item>Internal/trusted HTTP: 16–32.</item>
    ///   <item>S3 (same-region, same-bucket): 64–128 — S3 handles high parallelism well.</item>
    /// </list>
    /// TODO: When S3 storage is added, consider deriving the limit automatically from
    /// the URI scheme/host of the ALTO links, or adding a per-host override table here.
    /// </summary>
    public int MaxConcurrentAltoFetches { get; set; } = 8;

    /// <summary>Options for the filesystem text store.</summary>
    public StorageOptions Storage { get; set; } = new();
}

public class StorageOptions
{
    /// <summary>Root path under which text artefacts are stored on the filesystem.</summary>
    public string RootPath { get; set; } = "textservices-data";
}
