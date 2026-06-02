namespace TextServices.Infrastructure;

/// <summary>
/// Builds absolute URLs for all Search API endpoints. A single place to update if any
/// route segment changes.
/// </summary>
public static class SearchApiRoutes
{
    public static string SearchV1(string baseUrl, string id) => $"{baseUrl}/search/v1/{id}";
    public static string SearchV2(string baseUrl, string id) => $"{baseUrl}/search/v2/{id}";
    public static string AutocompleteV1(string baseUrl, string id) => $"{baseUrl}/autocomplete/v1/{id}";
    public static string AutocompleteV2(string baseUrl, string id) => $"{baseUrl}/autocomplete/v2/{id}";
    public static string FullText(string baseUrl, string id) => $"{baseUrl}/text/v1/{id}";
    public static string Pdf(string baseUrl, string id) => $"{baseUrl}/pdf/v1/{id}";
    public static string TextAugmented(string baseUrl, string id) => $"{baseUrl}/text-augmented/v3/{id}";
    public static string AnnotationsManifest(string baseUrl, string id) => $"{baseUrl}/annotations/manifest/v1/{id}";

    public static string AnnotationsLines(string baseUrl, string id, int canvasIndex) =>
        $"{baseUrl}/annotations/lines/v1/{canvasIndex}/{id}";

    public static string AnnotationsWords(string baseUrl, string id, int canvasIndex) =>
        $"{baseUrl}/annotations/words/v1/{canvasIndex}/{id}";

    public static string Figures(string baseUrl, string id) => $"{baseUrl}/identified/figures/{id}";
}
