namespace TextServices.Core.Models;

/// <summary>
/// The result of building a text index from a sequence of pages.
/// Wraps the two separate serialised artefacts: the search <see cref="Text"/>
/// and the <see cref="AutoComplete"/> index.
/// </summary>
public class TextBuildResult
{
    public static readonly TextBuildResult Empty = new() { IsEmpty = true };

    public Text Text { get; init; } = new();
    public AutoComplete AutoComplete { get; init; } = new();

    /// <summary>True when the source sequence contained no text content at all.</summary>
    public bool IsEmpty { get; init; }
}
