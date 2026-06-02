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
/// <param name="Resolved">
/// Resolved URL components for this request. <see cref="ResolvedRequest.EffectiveId"/> is used
/// for generated IIIF service URLs; it may differ from <see cref="Id"/> when the request arrived
/// via a proxy that rewrites the path (X-Forwarded-Path).
/// </param>
internal record TextAugmentedRequest(string Id, ResolvedRequest Resolved)
    : IRequest<JsonNode?>;

internal class TextAugmentedHandler(ITextStore textStore, ITextCache textCache, ILogger<TextAugmentedHandler> logger)
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

        var text = await textCache.GetTextAsync(request.Id, ct);

        var resolvedRequestUris = request.Resolved;
        ReplaceSelfUrl(manifest, resolvedRequestUris.SelfUrl);
        if (text != null)
        {
            InjectSearchServices(manifest, resolvedRequestUris);
            InjectRenderingLinks(manifest, resolvedRequestUris, text);
            InjectCanvasAnnotationRefs(manifest, resolvedRequestUris, text);
        }
        else
        {
            logger.LogDebug("Text not found: {Id}", request.Id);
        }

        await InjectManifestAnnotationsRefAsync(manifest, resolvedRequestUris, request.Id);
        await InjectFiguresRefAsync(manifest, resolvedRequestUris, request.Id);

        return manifest;
    }

    private static void ReplaceSelfUrl(JsonObject manifest, string selfUrl)
    {
        if (manifest.ContainsKey("@id"))
            manifest["@id"] = selfUrl;
        else
            manifest["id"] = selfUrl;
    }

    private void InjectSearchServices(JsonObject manifest, ResolvedRequest resolved)
    {
        logger.LogDebug("Adding search-services: {Id}", resolved.EffectiveId);
        var searchServiceV2 = new JsonObject
        {
            ["id"] = resolved.SearchV2Url(),
            ["type"] = "SearchService2",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"] = resolved.AutocompleteV2Url(),
                ["type"] = "AutoCompleteService2",
            }),
        };

        var searchServiceV1 = new JsonObject
        {
            ["id"] = resolved.SearchV1Url(),
            ["type"] = "SearchService1",
            ["profile"] = "http://iiif.io/api/search/1/search",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"] = resolved.AutocompleteV1Url(),
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

    private void InjectRenderingLinks(JsonObject manifest, ResolvedRequest resolved, Text text)
    {
        logger.LogDebug("Adding rendering: {Id}", resolved.EffectiveId);

        // PDF is only added when at least one canvas is image-based (IsTemporalContent == false);
        // temporal-only manifests (VTT/audio/video) have no page images to render into a PDF.
        var hasImageCanvases = text.Images.Any(img => !img.IsTemporalContent);

        var textRef = new JsonObject
        {
            ["id"] = resolved.FullTextUrl(),
            ["type"] = "Text",
            ["label"] = new JsonObject { ["en"] = new JsonArray("View as plain text") },
            ["format"] = "text/plain",
        };

        if (manifest["rendering"] is JsonArray existingRendering)
        {
            AddIfNew(existingRendering, textRef);
            if (hasImageCanvases) AddIfNew(existingRendering, BuildPdfRef(resolved));
        }
        else if (manifest["rendering"] is JsonObject singleRendering)
        {
            var arr = new JsonArray(singleRendering.DeepClone());
            AddIfNew(arr, textRef);
            if (hasImageCanvases) AddIfNew(arr, BuildPdfRef(resolved));
            manifest["rendering"] = arr;
        }
        else
        {
            manifest["rendering"] = hasImageCanvases
                ? new JsonArray(BuildPdfRef(resolved), textRef)
                : new JsonArray(textRef);
        }
    }

    private void InjectCanvasAnnotationRefs(JsonObject manifest, ResolvedRequest resolved, Text text)
    {
        if (manifest["items"] is not JsonArray canvases) return;

        logger.LogDebug("Adding canvas annotations: {Id}", resolved.EffectiveId);

        // Build a set of canvas indices that have at least one word, in O(words).
        var canvasesWithWords = text.Words.Values.Select(w => w.Idx).ToHashSet();

        for (var i = 0; i < canvases.Count && i < text.Images.Length; i++)
        {
            if (!canvasesWithWords.Contains(i)) continue;
            if (canvases[i] is not JsonObject canvas) continue;

            var linesRef = new JsonObject
            {
                ["id"] = resolved.AnnotationsLinesUrl(i),
                ["type"] = "AnnotationPage",
                ["label"] = new JsonObject { ["en"] = new JsonArray("Line-level transcription") },
            };
            var wordsRef = new JsonObject
            {
                ["id"] = resolved.AnnotationsWordsUrl(i),
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

    private async Task InjectManifestAnnotationsRefAsync(JsonObject manifest, ResolvedRequest resolved, string key)
    {
        var annotationsJson = await textStore.LoadAnnotations(key);
        if (annotationsJson == null) return;

        logger.LogDebug("Adding Manifest annotations: {Id}", resolved.EffectiveId);

        var annotationsRef = new JsonObject
        {
            ["id"] = resolved.AnnotationsManifestUrl(),
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

    private async Task InjectFiguresRefAsync(JsonObject manifest, ResolvedRequest resolved, string key)
    {
        var figuresJson = await textStore.LoadFigures(key);
        if (figuresJson == null) return;

        logger.LogDebug("Adding figures: {Id}", resolved.EffectiveId);

        var figuresRef = new JsonObject
        {
            ["id"] = resolved.FiguresUrl(),
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

    private static JsonObject BuildPdfRef(ResolvedRequest resolved) => new()
    {
        ["id"] = resolved.PdfUrl(),
        ["type"] = "Text",
        ["label"] = new JsonObject { ["en"] = new JsonArray("Download as PDF") },
        ["format"] = "application/pdf",
    };
}
