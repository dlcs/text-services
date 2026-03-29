using System.Text.Json;
using TextServices.Builder.Api.Features.Jobs;

namespace TextServices.Builder.Api.Services;

/// <summary>
/// Parses a IIIF Presentation v3 Manifest using <see cref="System.Text.Json"/>
/// and returns a flat page sequence. No full IIIF object model is needed —
/// the manifest is treated as plain JSON.
/// </summary>
public class ManifestReducer : IManifestReducer
{
    public IReadOnlyList<PageInstruction> Reduce(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;

        if (!IsV3(root))
            throw new InvalidOperationException(
                "Only IIIF Presentation v3 manifests are supported. " +
                "The manifest must have @context containing 'presentation/3'.");

        if (!root.TryGetProperty("items", out var items))
            return [];

        var pages = new List<PageInstruction>();

        foreach (var canvas in items.EnumerateArray())
        {
            if (!canvas.TryGetProperty("id", out var idEl)) continue;

            var id     = idEl.GetString() ?? string.Empty;
            var width  = canvas.TryGetProperty("width",  out var w) ? w.GetInt32() : 0;
            var height = canvas.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
            var alto   = FindAltoUri(canvas);

            pages.Add(new PageInstruction { Id = id, Width = width, Height = height, Text = alto });
        }

        return pages;
    }

    // -------------------------------------------------------------------------
    // v3 detection
    // -------------------------------------------------------------------------

    private static bool IsV3(JsonElement root)
    {
        if (root.TryGetProperty("@context", out var ctx))
        {
            switch (ctx.ValueKind)
            {
                case JsonValueKind.String:
                    return IsV3Context(ctx.GetString());

                case JsonValueKind.Array:
                    foreach (var item in ctx.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String && IsV3Context(item.GetString()))
                            return true;
                    // If the array contained a v2 context and no v3 context, reject.
                    return false;
            }
        }

        // No @context but has type="Manifest" + items array → treat as v3.
        return root.TryGetProperty("type", out var type) &&
               type.GetString() == "Manifest" &&
               root.TryGetProperty("items", out _);
    }

    private static bool IsV3Context(string? ctx) =>
        ctx != null && ctx.Contains("presentation/3", StringComparison.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // ALTO seeAlso detection
    // -------------------------------------------------------------------------

    private static string? FindAltoUri(JsonElement canvas)
    {
        if (!canvas.TryGetProperty("seeAlso", out var seeAlso))
            return null;

        return seeAlso.ValueKind switch
        {
            JsonValueKind.Array  => FindAltoInArray(seeAlso),
            JsonValueKind.Object => TryGetAltoUri(seeAlso),
            _                    => null,
        };
    }

    private static string? FindAltoInArray(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            var uri = TryGetAltoUri(item);
            if (uri != null) return uri;
        }
        return null;
    }

    private static string? TryGetAltoUri(JsonElement item)
    {
        var profile = item.TryGetProperty("profile", out var p) ? p.GetString() : null;
        var label   = item.TryGetProperty("label",   out var l) ? ExtractLabelText(l) : null;

        if (!IsAlto(profile, label)) return null;

        return item.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the profile URI or label text indicates an ALTO resource.
    /// Detection is intentionally broad: profile contains "alto" OR label contains "alto".
    /// </summary>
    private static bool IsAlto(string? profile, string? label) =>
        (profile != null && profile.Contains("alto", StringComparison.OrdinalIgnoreCase)) ||
        (label   != null && label  .Contains("alto", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extracts a plain string from a IIIF language map
    /// (<c>{"none": ["text"], "en": ["text"]}</c>) or returns the string directly.
    /// Returns the first value found, regardless of language key.
    /// </summary>
    private static string? ExtractLabelText(JsonElement label)
    {
        if (label.ValueKind == JsonValueKind.String)
            return label.GetString();

        if (label.ValueKind == JsonValueKind.Object)
        {
            foreach (var lang in label.EnumerateObject())
            {
                if (lang.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var val in lang.Value.EnumerateArray())
                        if (val.ValueKind == JsonValueKind.String)
                            return val.GetString();
                }
                else if (lang.Value.ValueKind == JsonValueKind.String)
                {
                    return lang.Value.GetString();
                }
            }
        }

        return null;
    }
}
