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
    /// Maximum number of Text objects to hold in the memory cache simultaneously.
    /// Each Text entry (regardless of size) counts as one slot. When full, the
    /// least-recently-used entry is evicted. AutoComplete objects share the same
    /// slot budget and are each counted as one slot.
    /// Default: 20 (sufficient for a lightly-loaded service; raise for busier deployments,
    /// but budget ~30–40 MB per large text when sizing ECS task memory).
    /// </summary>
    public int CacheMaxEntries { get; set; } = 20;

    /// <summary>Maximum number of background PDF trigger requests to queue. Requests beyond this capacity return 503.</summary>
    public int PdfTriggerQueueCapacity { get; set; } = 50;

    /// <summary>
    /// Maximum number of PDF generations to run concurrently in the background trigger queue.
    /// Each in-flight generation buffers the full PDF in memory, so keep this low on memory-constrained hosts.
    /// </summary>
    public int PdfTriggerMaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Allow <c>GET /proxy/image</c> to serve <c>file://</c> image URIs.
    /// <para>
    /// When <c>false</c> (the default): requests for <c>file://</c> URIs return a 1×1
    /// placeholder PNG — the file is never read, regardless of what <c>uri=</c> contains.
    /// This is the safe default; it prevents the proxy from exposing access-controlled images
    /// even if a proxy URL somehow ends up in an untrusted manifest.
    /// </para>
    /// <para>
    /// When <c>true</c>: the proxy reads and streams the local file.  Only enable in trusted
    /// environments (e.g. local development) where the referenced files are not access-controlled.
    /// The Builder API's <c>AllowFileImageProxy</c> must also be <c>true</c> for proxy URLs to
    /// be emitted in synthesised Manifests in the first place.
    /// </para>
    /// </summary>
    public bool AllowFileImageProxy { get; set; } = false;

    /// <summary>
    /// Hostnames accepted from the <c>X-Forwarded-Host</c> request header (e.g. custom CloudFront distributions).
    /// When a request carries <c>X-Forwarded-Host</c> and its value matches an entry here, that host
    /// replaces the canonical host in generated IIIF URLs. An empty array (the default) means
    /// <c>X-Forwarded-Host</c> is never honoured.
    /// </summary>
    public string[] AllowedCustomHosts { get; set; } = [];

    /// <summary>Options for the text artefact store.</summary>
    public SearchStorageOptions Storage { get; set; } = new();
}

public class SearchStorageOptions
{
    /// <summary>Options for the filesystem text store.</summary>
    public FileSystemStorageOptions FileSystem { get; set; } = new();

    /// <summary>Options for S3 storage. When <see cref="S3StorageOptions.BucketName"/> is set, S3 is used instead of the filesystem.</summary>
    public S3StorageOptions S3 { get; set; } = new();
}

public class FileSystemStorageOptions
{
    /// <summary>Root directory for stored text artefacts.</summary>
    public string RootPath { get; set; } = "textservices-data";
}

public class S3StorageOptions
{
    /// <summary>S3 bucket for stored artefacts. When set, the S3 store is used instead of the filesystem store.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Optional prefix for all S3 object keys (e.g. "textservices/"). A trailing / is added automatically if omitted.</summary>
    public string KeyPrefix { get; set; } = string.Empty;
}
