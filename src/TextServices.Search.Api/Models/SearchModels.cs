using System.Text.Json.Serialization;

namespace TextServices.Search.Api.Models;

// ---- IIIF Search API v1 response types ----------------------------------------
// Spec: https://iiif.io/api/search/1.0/
// Property names use JSON-LD @-prefixed keys as required by the spec.

public class SearchAnnotationList
{
    [JsonPropertyName("@context")]
    public string Context { get; set; } = "http://iiif.io/api/search/1/context.json";

    [JsonPropertyName("@id")]
    public required string Id { get; set; }

    [JsonPropertyName("@type")]
    public string Type { get; set; } = "sc:AnnotationList";

    [JsonPropertyName("within")]
    public SearchLayer Within { get; set; } = new();

    [JsonPropertyName("resources")]
    public List<SearchAnnotation> Resources { get; set; } = [];

    [JsonPropertyName("hits")]
    public List<SearchHit> Hits { get; set; } = [];

    /// <summary>
    /// Recognised-but-unsupported query parameters (motivation, date, user, box).
    /// Omitted from the response when null.
    /// </summary>
    [JsonPropertyName("ignored")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Ignored { get; set; }
}

public class SearchLayer
{
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "sc:Layer";

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class SearchAnnotation
{
    [JsonPropertyName("@id")]
    public required string Id { get; set; }

    [JsonPropertyName("@type")]
    public string Type { get; set; } = "oa:Annotation";

    [JsonPropertyName("motivation")]
    public string Motivation { get; set; } = "sc:painting";

    [JsonPropertyName("resource")]
    public required SearchAnnotationResource Resource { get; set; }

    /// <summary>Canvas URI + xywh spatial fragment, e.g. "https://example.org/canvas/1#xywh=10,20,50,30".</summary>
    [JsonPropertyName("on")]
    public required string On { get; set; }
}

public class SearchAnnotationResource
{
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "cnt:ContentAsText";

    [JsonPropertyName("chars")]
    public required string Chars { get; set; }
}

public class SearchHit
{
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "search:Hit";

    [JsonPropertyName("annotations")]
    public required string[] Annotations { get; set; }

    [JsonPropertyName("match")]
    public string Match { get; set; } = string.Empty;

    [JsonPropertyName("before")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Before { get; set; }

    [JsonPropertyName("after")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? After { get; set; }
}

// ---- IIIF Autocomplete v1 response types --------------------------------------

public class AutocompleteTermList
{
    [JsonPropertyName("@context")]
    public string Context { get; set; } = "http://iiif.io/api/search/1/context.json";

    [JsonPropertyName("@id")]
    public required string Id { get; set; }

    [JsonPropertyName("@type")]
    public string Type { get; set; } = "search:TermList";

    [JsonPropertyName("terms")]
    public List<AutocompleteTerm> Terms { get; set; } = [];
}

public class AutocompleteTerm
{
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "search:Term";

    [JsonPropertyName("match")]
    public required string Match { get; set; }
}
