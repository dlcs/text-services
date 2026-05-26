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
    /// Allow the Search API's <c>/proxy/image</c> endpoint to serve <c>file://</c> image URIs
    /// supplied in <c>sourceData</c> pages.
    /// <para>
    /// When <c>false</c> (the default): painting annotations for <c>file://</c> and <c>s3://</c>
    /// imageUris are omitted from synthesised Manifests entirely — no file path is embedded in
    /// the manifest and no image is served.  The text index and all other endpoints are unaffected.
    /// </para>
    /// <para>
    /// When <c>true</c>: the synthesised Manifest includes a painting annotation whose
    /// <c>body.id</c> is a <c>/proxy/image?uri=…</c> URL on the Search API.  The Search API must
    /// also have <c>AllowFileImageProxy: true</c> for that URL to return real file content.
    /// Only enable this in trusted environments (e.g. local development) where the files
    /// referenced by <c>imageUri</c> are not access-controlled.
    /// </para>
    /// </summary>
    public bool AllowFileImageProxy { get; set; } = false;

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

    /// <summary>Options for job completion notifications.</summary>
    public NotificationsOptions Notifications { get; set; } = new();
}

public class StorageOptions
{
    /// <summary>Root path under which text artefacts are stored on the filesystem.</summary>
    public string RootPath { get; set; } = "textservices-data";

    /// <summary>
    /// S3 storage options. When <see cref="S3StorageOptions.BucketName"/> is set,
    /// the S3 store is used; otherwise the filesystem store is used.
    /// </summary>
    public S3StorageOptions? S3 { get; set; }
}

public class S3StorageOptions
{
    /// <summary>The S3 bucket name in which all artefacts are stored.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional key prefix applied to every S3 object key (e.g. <c>"textservices/"</c>).
    /// A trailing <c>/</c> is added automatically if omitted.
    /// Leave empty to store objects at the bucket root.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;
}

public class NotificationsOptions
{
    /// <summary>
    /// ARN of the SNS topic to publish job completion notifications to.
    /// Leave null or empty to disable notifications.
    /// </summary>
    public string? TopicArn { get; set; }
}
