using TextServices.Infrastructure;

namespace TextServices.Search.Api.Features;

/// <summary>
/// Holds the resolved URL components for a single endpoint request, accounting for
/// <c>X-Forwarded-Host</c> and <c>X-Forwarded-Path</c> proxy headers.
/// </summary>
/// <param name="EffectiveId">
/// Job id to use in generated IIIF URLs. Extracted from <c>X-Forwarded-Path</c> when a
/// whitelisted host is present; otherwise equals the original route id.
/// </param>
/// <param name="SelfUrl">Absolute URL for this endpoint response (base + route prefix + effective id + query).</param>
/// <param name="ResourceUrl">Absolute URL without query string. Used as the base for child resource IDs (e.g. annotations).</param>
/// <param name="BaseUrl">Scheme + authority only (no path). Configured URL or based on x-forwarded-host if valid.</param>
internal record ResolvedRequest(string EffectiveId, string SelfUrl, string ResourceUrl, string BaseUrl);

/// <summary>
/// Extension methods for <see cref="ResolvedRequest"/> to ease route generation
/// </summary>
internal static class ResolvedRequestExtensions
{
    public static string SearchV1Url(this ResolvedRequest r) => SearchApiRoutes.SearchV1(r.BaseUrl, r.EffectiveId);
    public static string SearchV2Url(this ResolvedRequest r) => SearchApiRoutes.SearchV2(r.BaseUrl, r.EffectiveId);

    public static string AutocompleteV1Url(this ResolvedRequest r) =>
        SearchApiRoutes.AutocompleteV1(r.BaseUrl, r.EffectiveId);

    public static string AutocompleteV2Url(this ResolvedRequest r) =>
        SearchApiRoutes.AutocompleteV2(r.BaseUrl, r.EffectiveId);

    public static string FullTextUrl(this ResolvedRequest r) => SearchApiRoutes.FullText(r.BaseUrl, r.EffectiveId);
    public static string PdfUrl(this ResolvedRequest r) => SearchApiRoutes.Pdf(r.BaseUrl, r.EffectiveId);

    public static string TextAugmentedUrl(this ResolvedRequest r) =>
        SearchApiRoutes.TextAugmented(r.BaseUrl, r.EffectiveId);

    public static string AnnotationsManifestUrl(this ResolvedRequest r) =>
        SearchApiRoutes.AnnotationsManifest(r.BaseUrl, r.EffectiveId);

    public static string AnnotationsLinesUrl(this ResolvedRequest r, int i) =>
        SearchApiRoutes.AnnotationsLines(r.BaseUrl, r.EffectiveId, i);

    public static string AnnotationsWordsUrl(this ResolvedRequest r, int i) =>
        SearchApiRoutes.AnnotationsWords(r.BaseUrl, r.EffectiveId, i);

    public static string FiguresUrl(this ResolvedRequest r) => SearchApiRoutes.Figures(r.BaseUrl, r.EffectiveId);
}
