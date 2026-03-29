namespace TextServices.Storage;

/// <summary>
/// Configuration options for <see cref="FileSystemTextStore"/>.
/// </summary>
public class FileSystemTextStoreOptions
{
    /// <summary>
    /// The root directory under which all artefacts are stored.
    /// Key segments (split on <c>/</c>) become nested subdirectories.
    /// </summary>
    public required string RootPath { get; init; }
}
