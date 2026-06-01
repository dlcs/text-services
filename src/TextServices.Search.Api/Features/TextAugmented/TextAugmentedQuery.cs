using System.Text.Json.Nodes;
using MediatR;
using TextServices.Core.Models;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.TextAugmented;

/// <summary>
/// Returns the stored IIIF Manifest JSON decorated with IIIF Search service descriptors
/// for both v2 and v1.  Services use the IIIF Presentation 3 id/type conventions:
///   type "SearchService2" / "AutoCompleteService2"  (IIIF Search 2)
///   type "SearchService1" / "AutoCompleteService1"  (IIIF Search 1, legacy)
/// No @context is emitted inside service blocks — it belongs only at document level.
/// </summary>
/// <param name="Id">Storage key used to load artefacts from the text store.</param>
/// <param name="SelfUrl">Absolute URL for this endpoint response (base + route prefix + effective id + query).</param>
/// <param name="SearchBaseUrl">
/// Scheme + authority only (no path). Used by TextAugmented to build cross-endpoint service URLs
/// </param>
/// <param name="UrlId">
/// Id to use when generating IIIF service URLs. Differs from <see cref="Id"/> when the
/// request arrived via a proxy that rewrites the path (X-Forwarded-Path). Defaults to
/// <see cref="Id"/> when null.
/// </param>
public record TextAugmentedRequest(string Id, string SelfUrl, string SearchBaseUrl, string? UrlId = null)
    : IRequest<JsonNode?>;

public class TextAugmentedHandler(ITextStore textStore, ITextCache textCache, ILogger<TextAugmentedHandler> logger)
    : IRequestHandler<TextAugmentedRequest, JsonNode?>
{
    public async Task<JsonNode?> Handle(TextAugmentedRequest request, CancellationToken ct)
    {
        if (!await textCache.IsEnabledAsync(request.Id, JobServices.TextAugmented, ct))
        {
            logger.LogDebug("Text augmentation is not enabled: {Id}", request.Id);
            return null;
        }
        var json = await textStore.LoadManifest(request.Id);
        if (json == null)
        {
            logger.LogDebug("Manifest not found: {Id}", request.Id);
            return null;
        }

        var node = JsonNode.Parse(json);
        if (node is not JsonObject manifest)
        {
            logger.LogDebug("Manifest not JSON: {Id}", request.Id);
            return null;
        }

        var baseUrl = request.SearchBaseUrl;
        var id = request.UrlId ?? request.Id;
        var text = await textCache.GetTextAsync(request.Id, ct);

        ReplaceSelfUrl(manifest, request.SelfUrl);
        if (text != null)
        {
            InjectSearchServices(manifest, baseUrl, id);
            InjectRenderingLinks(manifest, baseUrl, id, text);
            InjectCanvasAnnotationRefs(manifest, baseUrl, id, text);
        }
        else
        {
            logger.LogDebug("Text not found: {Id}", request.Id);
        }

        await InjectManifestAnnotationsRefAsync(manifest, baseUrl, id, request.Id);
        await InjectFiguresRefAsync(manifest, baseUrl, id, request.Id);

        return manifest;
    }

    private static void ReplaceSelfUrl(JsonObject manifest, string selfUrl)
    {
        if (manifest.ContainsKey("@id"))
            manifest["@id"] = selfUrl;
        else
            manifest["id"] = selfUrl;
    }

    private void InjectSearchServices(JsonObject manifest, string baseUrl, string id)
    {
        logger.LogDebug("Adding search-services: {Id}", id);
        var searchServiceV2 = new JsonObject
        {
            ["id"] = $"{baseUrl}/search/v2/{id}", // TODO - can we build these from a central place?
            ["type"] = "SearchService2",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"] = $"{baseUrl}/autocomplete/v2/{id}",
                ["type"] = "AutoCompleteService2",
            }),
        };

        var searchServiceV1 = new JsonObject
        {
            ["id"] = $"{baseUrl}/search/v1/{id}",
            ["type"] = "SearchService1",
            ["profile"] = "http://iiif.io/api/search/1/search",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"] = $"{baseUrl}/autocomplete/v1/{id}",
                ["type"] = "AutoCompleteService1",
                ["profile"] = "http://iiif.io/api/search/1/autocomplete",
            }),
        };

        // Insert v2 then v1 at position 0 so v2 appears first.
        // Existing services (e.g. IIIF Image services) are pushed down, not displaced.
        // Deduplication: skip insertion when a service with the same "id" already exists.
        if (manifest["service"] is JsonArray existingArray)
        {
            AddIfNew(existingArray, searchServiceV1);
            AddIfNew(existingArray, searchServiceV2);
        }
        else if (manifest["service"] is JsonObject existingObject)
        {
            // Spec allows service to be a single object — promote to array.
            var arr = new JsonArray(existingObject.DeepClone());
            AddIfNew(arr, searchServiceV1);
            AddIfNew(arr, searchServiceV2);
            manifest["service"] = arr;
        }
        else
        {
            manifest["service"] = new JsonArray(searchServiceV2, searchServiceV1);
        }

        AppendContext(manifest, "http://iiif.io/api/search/2/context.json");
        AppendContext(manifest, "http://iiif.io/api/search/1/context.json");
    }

    private void InjectRenderingLinks(JsonObject manifest, string baseUrl, string id, Text text)
    {
        logger.LogDebug("Adding rendering: {Id}", id);

        // PDF is only added when at least one canvas is image-based (IsTemporalContent == false);
        // temporal-only manifests (VTT/audio/video) have no page images to render into a PDF.
        var hasImageCanvases = text.Images.Any(img => !img.IsTemporalContent);

        var textRef = new JsonObject
        {
            ["id"] = $"{baseUrl}/text/v1/{id}",
            ["type"] = "Text",
            ["label"] = new JsonObject { ["en"] = new JsonArray("View as plain text") },
            ["format"] = "text/plain",
        };

        if (manifest["rendering"] is JsonArray existingRendering)
        {
            AddIfNew(existingRendering, textRef);
            if (hasImageCanvases) AddIfNew(existingRendering, BuildPdfRef(baseUrl, id));
        }
        else if (manifest["rendering"] is JsonObject singleRendering)
        {
            var arr = new JsonArray(singleRendering.DeepClone());
            AddIfNew(arr, textRef);
            if (hasImageCanvases) AddIfNew(arr, BuildPdfRef(baseUrl, id));
            manifest["rendering"] = arr;
        }
        else
        {
            manifest["rendering"] = hasImageCanvases
                ? new JsonArray(BuildPdfRef(baseUrl, id), textRef)
                : new JsonArray(textRef);
        }
    }

    private void InjectCanvasAnnotationRefs(JsonObject manifest, string baseUrl, string id, Text text)
    {
        if (manifest["items"] is not JsonArray canvases) return;

        logger.LogDebug("Adding canvas annotations: {Id}", id);

        // Build a set of canvas indices that have at least one word, in O(words).
        var canvasesWithWords = text.Words.Values.Select(w => w.Idx).ToHashSet();

        for (var i = 0; i < canvases.Count && i < text.Images.Length; i++)
        {
            if (!canvasesWithWords.Contains(i)) continue;
            if (canvases[i] is not JsonObject canvas) continue;

            var linesRef = new JsonObject
            {
                ["id"] = $"{baseUrl}/annotations/lines/v1/{i}/{id}",
                ["type"] = "AnnotationPage",
                ["label"] = new JsonObject { ["en"] = new JsonArray("Line-level transcription") },
            };
            var wordsRef = new JsonObject
            {
                ["id"] = $"{baseUrl}/annotations/words/v1/{i}/{id}",
                ["type"] = "AnnotationPage",
                ["label"] = new JsonObject { ["en"] = new JsonArray("Word-level transcription") },
            };

            if (canvas["annotations"] is JsonArray existingAnnos)
            {
                AddIfNew(existingAnnos, wordsRef);
                AddIfNew(existingAnnos, linesRef);
            }
            else if (canvas["annotations"] is JsonObject singleAnno)
            {
                var arr = new JsonArray(singleAnno.DeepClone());
                AddIfNew(arr, wordsRef);
                AddIfNew(arr, linesRef);
                canvas["annotations"] = arr;
            }
            else
            {
                canvas["annotations"] = new JsonArray(linesRef, wordsRef);
            }
        }
    }

    private async Task InjectManifestAnnotationsRefAsync(JsonObject manifest, string baseUrl, string id, string key)
    {
        var annotationsJson = await textStore.LoadAnnotations(key);
        if (annotationsJson == null) return;

        logger.LogDebug("Adding Manifest annotations: {Id}", id);

        var annotationsRef = new JsonObject
        {
            ["id"] = $"{baseUrl}/annotations/manifest/v1/{id}",
            ["type"] = "AnnotationPage",
            ["profile"] = "https://dlcs.io/profiles/all-text",
            ["label"] = new JsonObject { ["en"] = new JsonArray("Text of all canvases") },
        };

        if (manifest["annotations"] is JsonArray existingAnnos)
        {
            AddIfNew(existingAnnos, annotationsRef);
        }
        else if (manifest["annotations"] is JsonObject singleAnno)
        {
            var arr = new JsonArray(singleAnno.DeepClone());
            AddIfNew(arr, annotationsRef);
            manifest["annotations"] = arr;
        }
        else
        {
            manifest["annotations"] = new JsonArray(annotationsRef);
        }
    }

    private async Task InjectFiguresRefAsync(JsonObject manifest, string baseUrl, string id, string key)
    {
        var figuresJson = await textStore.LoadFigures(key);
        if (figuresJson == null) return;

        logger.LogDebug("Adding figures: {Id}", id);

        var figuresRef = new JsonObject
        {
            ["id"] = $"{baseUrl}/identified/figures/{id}",
            ["type"] = "AnnotationPage",
            ["label"] = new JsonObject
            {
                ["en"] = new JsonArray("Figures, tables and illustrations"),
            },
        };

        if (manifest["annotations"] is JsonArray existingAnnos)
        {
            AddIfNew(existingAnnos, figuresRef, prepend: false);
        }
        else if (manifest["annotations"] is JsonObject singleAnno)
        {
            var arr = new JsonArray(singleAnno.DeepClone());
            AddIfNew(arr, figuresRef, prepend: false);
            manifest["annotations"] = arr;
        }
        else
        {
            manifest["annotations"] = new JsonArray(figuresRef);
        }
    }

    private static void AddIfNew(JsonArray array, JsonObject item, bool prepend = true)
    {
        if (array.Any(n => n?["id"]?.GetValue<string>() == item["id"]?.GetValue<string>())) return;
        if (prepend) array.Insert(0, item);
        else array.Add(item);
    }

    private static void AppendContext(JsonObject manifest, string url)
    {
        if (manifest["@context"] is JsonArray arr)
        {
            if (!arr.Any(n => n?.GetValue<string>() == url))
                arr.Add(url);
        }
        else if (manifest["@context"] is JsonValue str)
        {
            manifest["@context"] = new JsonArray(str.GetValue<string>(), url);
        }
        else
        {
            manifest["@context"] = new JsonArray(url);
        }
    }

    private static JsonObject BuildPdfRef(string baseUrl, string id) => new()
    {
        ["id"] = $"{baseUrl}/pdf/v1/{id}",
        ["type"] = "Text",
        ["label"] = new JsonObject { ["en"] = new JsonArray("Download as PDF") },
        ["format"] = "application/pdf",
    };
}
