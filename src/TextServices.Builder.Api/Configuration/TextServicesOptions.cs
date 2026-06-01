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
    /// Maximum number of text files (ALTO, VTT, AnnotationPage) fetched concurrently within a single job.
    /// The right value depends on the source:
    /// <list type="bullet">
    ///   <item>Third-party HTTP (Wellcome, Internet Archive, etc.): 4–8 for politeness.</item>
    ///   <item>Internal/trusted HTTP: 16–32.</item>
    ///   <item>S3 (same-region, same-bucket): 64–128 — S3 handles high parallelism well.</item>
    /// </list>
    /// TODO: When S3 storage is added, consider deriving the limit automatically from
    /// the URI scheme/host of the text links, or adding a per-host override table here.
    /// </summary>
    public int MaxConcurrentPageFetches { get; set; } = 8;

    /// <summary>
    /// When <c>true</c> (the default), job progress is flushed to the database every
    /// <c>ProgressBatchSize</c> pages so that <c>GET /textbuilder/{id}</c> reflects live
    /// in-flight progress. Set to <c>false</c> to reduce database writes on large manifests
    /// at the cost of no progress visibility until the job completes.
    /// </summary>
    public bool ReportBatchProgress { get; set; } = true;

    /// <summary>Options for the filesystem text store.</summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>Options for job completion notifications.</summary>
    public NotificationsOptions Notifications { get; set; } = new();
}

public class StorageOptions
{
    /// <summary>Options for the filesystem text store.</summary>
    public FileSystemStorageOptions FileSystem { get; set; } = new();

    /// <summary>Options for S3 storage. When <see cref="S3StorageOptions.BucketName"/> is set, S3 is used instead of the filesystem.</summary>
    public S3StorageOptions S3 { get; set; } = new();
}

public class FileSystemStorageOptions
{
    /// <summary>Root path under which text artefacts are stored on the filesystem.</summary>
    public string RootPath { get; set; } = "textservices-data";
}

public class S3StorageOptions
{
    /// <summary>S3 bucket for stored artefacts.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Optional prefix for all S3 object keys (e.g. "textservices/"). A trailing / is added automatically if omitted.</summary>
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
