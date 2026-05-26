namespace TextServices.Storage;

/// <summary>
/// Configuration options for <see cref="S3TextStore"/>.
/// </summary>
public class S3TextStoreOptions
{
    /// <summary>
    /// The S3 bucket name in which all artefacts are stored.
    /// </summary>
    public required string BucketName { get; init; }

    /// <summary>
    /// Optional key prefix applied to every S3 object key (e.g. <c>"textservices/"</c>).
    /// A trailing <c>/</c> is added automatically if omitted.
    /// Leave empty to store objects at the bucket root.
    /// </summary>
    public string KeyPrefix { get; init; } = string.Empty;

}
