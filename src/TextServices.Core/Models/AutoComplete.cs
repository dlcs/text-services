using ProtoBuf;

namespace TextServices.Core.Models;

/// <summary>
/// Autocomplete index for a document. Stored as a separate Protobuf file from <see cref="Text"/>
/// so the Search API can load it independently for autocomplete-only requests.
/// </summary>
[ProtoContract]
public class AutoComplete
{
    /// <summary>
    /// 3-character prefix buckets mapping the first 3 characters of a normalised word
    /// to the set of all normalised words in the document that share that prefix.
    /// </summary>
    [ProtoMember(1)] public Dictionary<string, HashSet<string>> Buckets { get; set; } = new();

    /// <summary>True if no words have been indexed.</summary>
    public bool IsEmpty => Buckets.Count == 0;

    /// <summary>
    /// Returns autocomplete suggestions for the given term, ordered by word length then alphabetically.
    /// Returns an empty array if the normalised term is shorter than 3 characters.
    /// </summary>
    public string[] GetSuggestions(string term)
    {
        var normTerm = Text.Normalise(term);
        if (normTerm.Length < 3) return [];

        var prefix = normTerm[..3];
        if (!Buckets.TryGetValue(prefix, out var bucket)) return [];

        return [.. bucket
            .Where(w => w.StartsWith(normTerm, StringComparison.Ordinal))
            .OrderBy(w => w.Length)
            .ThenBy(w => w, StringComparer.Ordinal)];
    }
}
