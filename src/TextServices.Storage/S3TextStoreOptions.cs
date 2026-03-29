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

    /// <summary>
    /// AWS region name (e.g. <c>"eu-west-1"</c>).
    /// When running on ECS/EC2 with an appropriate IAM role this can be left empty
    /// and the SDK will resolve the region from instance metadata.
    /// </summary>
    public string RegionName { get; init; } = string.Empty;
}
