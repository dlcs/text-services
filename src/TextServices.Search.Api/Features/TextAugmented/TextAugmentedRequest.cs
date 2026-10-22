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

        // A PDF doesn't need text, so its link is offered for any job with image canvases.
        // Temporal-only manifests (VTT/audio/video) have no page images to render into a PDF.
        var includePdf = await textCache.IsEnabledAsync(request.Id, JobServices.Pdf, ct)
                         && (text?.Images.Any(img => !img.IsTemporalContent) == true || HasImageCanvas(manifest));

        if (text != null)
        {
            InjectSearchServices(manifest, resolvedRequestUris);
            InjectRenderingLinks(manifest, resolvedRequestUris, includeText: true, includePdf);
            InjectCanvasAnnotationRefs(manifest, resolvedRequestUris, text);
            if (await textCache.IsEnabledAsync(request.Id, JobServices.Svg, ct))
                InjectCanvasSvgRenderings(manifest, resolvedRequestUris, text);
        }
        else
        {
            logger.LogDebug("Text not found: {Id}", request.Id);
            if (includePdf)
            {
                InjectRenderingLinks(manifest, resolvedRequestUris, includeText: false, includePdf: true);
            }
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

        PrependContext(manifest, "http://iiif.io/api/search/1/context.json");
        PrependContext(manifest, "http://iiif.io/api/search/2/context.json");
    }

    private void InjectRenderingLinks(JsonObject manifest, ResolvedRequest resolved, bool includeText, bool includePdf)
    {
        logger.LogDebug("Adding rendering: {Id}", resolved.EffectiveId);

        // Ordered as they appear in a new rendering array: PDF first, then plain text.
        var refs = new List<JsonObject>();
        if (includePdf) refs.Add(BuildPdfRef(resolved));
        if (includeText)
        {
            refs.Add(new JsonObject
            {
                ["id"] = resolved.FullTextUrl(),
                ["type"] = "Text",
                ["label"] = new JsonObject { ["en"] = new JsonArray("View as plain text") },
                ["format"] = "text/plain",
            });
        }

        if (manifest["rendering"] is JsonArray existingRendering)
        {
            // AddIfNew prepends, so add in reverse to keep PDF before text.
            for (var i = refs.Count - 1; i >= 0; i--) AddIfNew(existingRendering, refs[i]);
        }
        else if (manifest["rendering"] is JsonObject singleRendering)
        {
            var arr = new JsonArray(singleRendering.DeepClone());
            for (var i = refs.Count - 1; i >= 0; i--) AddIfNew(arr, refs[i]);
            manifest["rendering"] = arr;
        }
        else
        {
            manifest["rendering"] = new JsonArray([.. refs]);
        }
    }

    /// <summary>True when any canvas is painted with an image (i.e. can become a PDF page).</summary>
    private static bool HasImageCanvas(JsonObject manifest)
    {
        foreach (var canvas in manifest["items"] as JsonArray ?? [])
        {
            foreach (var annoPage in canvas?["items"] as JsonArray ?? [])
            {
                foreach (var anno in annoPage?["items"] as JsonArray ?? [])
                {
                    var body = anno?["body"];
                    if (body is JsonArray arr) body = arr.FirstOrDefault();
                    var type = body?["type"]?.GetValue<string>() ?? body?["@type"]?.GetValue<string>();
                    if (string.Equals(type, "Image", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        return false;
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

    private void InjectCanvasSvgRenderings(JsonObject manifest, ResolvedRequest resolved, Text text)
    {
        if (manifest["items"] is not JsonArray canvases) return;

        logger.LogDebug("Adding canvas SVG renderings: {Id}", resolved.EffectiveId);

        // SVG is only meaningful for spatial canvases that have at least one word.
        var canvasesWithWords = text.Words.Values.Select(w => w.Idx).ToHashSet();

        for (var i = 0; i < canvases.Count && i < text.Images.Length; i++)
        {
            if (text.Images[i].IsTemporalContent) continue;
            if (!canvasesWithWords.Contains(i)) continue;
            if (canvases[i] is not JsonObject canvas) continue;

            var svgRef = new JsonObject
            {
                ["id"] = resolved.SvgUrl(i),
                ["type"] = "Image",
                ["label"] = new JsonObject { ["en"] = new JsonArray("SVG XML for page text") },
                ["format"] = "image/svg+xml",
            };

            if (canvas["rendering"] is JsonArray existingRendering)
            {
                AddIfNew(existingRendering, svgRef, prepend: false);
            }
            else if (canvas["rendering"] is JsonObject singleRendering)
            {
                var arr = new JsonArray(singleRendering.DeepClone());
                AddIfNew(arr, svgRef, prepend: false);
                canvas["rendering"] = arr;
            }
            else
            {
                canvas["rendering"] = new JsonArray(svgRef);
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

    private static void PrependContext(JsonObject manifest, string url)
    {
        if (manifest["@context"] is JsonArray arr)
        {
            if (arr.All(n => n?.GetValue<string>() != url))
                arr.Insert(0, url);
        }
        else if (manifest["@context"] is JsonValue str)
        {
            manifest["@context"] = new JsonArray(url, str.GetValue<string>());
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
