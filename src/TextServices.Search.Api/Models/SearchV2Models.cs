using System.Text.Json.Serialization;

namespace TextServices.Search.Api.Models;

// ---- IIIF Content Search API v2 response types --------------------------------
// Spec: https://iiif.io/api/search/2.0/
// Uses modern JSON-LD conventions: id/type instead of @id/@type.

// ---- Search response (AnnotationPage) ----------------------------------------

public class SearchAnnotationPageV2
{
    [JsonPropertyName("@context")]
    public string Context { get; } = "http://iiif.io/api/search/2/context.json";

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; } = "AnnotationPage";

    [JsonPropertyName("items")]
    public List<PaintingAnnotationV2> Items { get; set; } = [];

    /// <summary>
    /// AnnotationPages containing contextualizing annotations (before/match/after context).
    /// Omitted when there are no results.
    /// </summary>
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ContextualizingAnnotationPage>? Annotations { get; set; }

    /// <summary>Recognised-but-unsupported query parameters. Omitted when null.</summary>
    [JsonPropertyName("ignored")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Ignored { get; set; }
}

public class PaintingAnnotationV2
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; } = "Annotation";

    [JsonPropertyName("motivation")]
    public string Motivation { get; } = "painting";

    [JsonPropertyName("body")]
    public required TextualBodyV2 Body { get; set; }

    /// <summary>Canvas URI + xywh spatial fragment.</summary>
    [JsonPropertyName("target")]
    public required string Target { get; set; }
}

public class TextualBodyV2
{
    [JsonPropertyName("type")]
    public string Type { get; } = "TextualBody";

    [JsonPropertyName("value")]
    public required string Value { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; } = "text/plain";
}

// ---- Contextualizing annotations (hit context) --------------------------------

public class ContextualizingAnnotationPage
{
    [JsonPropertyName("type")]
    public string Type { get; } = "AnnotationPage";

    [JsonPropertyName("items")]
    public List<ContextualizingAnnotation> Items { get; set; } = [];
}

public class ContextualizingAnnotation
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; } = "Annotation";

    [JsonPropertyName("motivation")]
    public string Motivation { get; } = "contextualizing";

    [JsonPropertyName("target")]
    public required SpecificResourceTarget Target { get; set; }
}

public class SpecificResourceTarget
{
    [JsonPropertyName("type")]
    public string Type { get; } = "SpecificResource";

    /// <summary>ID of the painting annotation this context applies to.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; set; }

    [JsonPropertyName("selector")]
    public required TextQuoteSelector[] Selector { get; set; }
}

public class TextQuoteSelector
{
    [JsonPropertyName("type")]
    public string Type { get; } = "TextQuoteSelector";

    [JsonPropertyName("exact")]
    public required string Exact { get; set; }

    [JsonPropertyName("prefix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Prefix { get; set; }

    [JsonPropertyName("suffix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Suffix { get; set; }
}

// ---- Autocomplete response (TermPage) ----------------------------------------

public class TermPageV2
{
    [JsonPropertyName("@context")]
    public string Context { get; } = "http://iiif.io/api/search/2/context.json";

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; } = "TermPage";

    [JsonPropertyName("items")]
    public List<TermV2> Items { get; set; } = [];
}

public class TermV2
{
    [JsonPropertyName("value")]
    public required string Value { get; set; }
}
