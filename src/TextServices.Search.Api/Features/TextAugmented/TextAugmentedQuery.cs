using System.Text.Json.Nodes;
using MediatR;
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
public record TextAugmentedRequest(string Id, string SelfUrl, string SearchBaseUrl)
    : IRequest<JsonNode?>;

public class TextAugmentedHandler(ITextStore textStore, ITextCache textCache)
    : IRequestHandler<TextAugmentedRequest, JsonNode?>
{
    public async Task<JsonNode?> Handle(TextAugmentedRequest request, CancellationToken ct)
    {
        if (!await textCache.IsEnabledAsync(request.Id, JobServices.TextAugmented, ct)) return null;
        var json = await textStore.LoadManifest(request.Id);
        if (json == null) return null;

        var node = JsonNode.Parse(json);
        if (node is not JsonObject manifest) return null;

        // Replace @id / id with the text-augmented URL for this manifest.
        if (manifest.ContainsKey("@id"))
            manifest["@id"] = request.SelfUrl;
        else
            manifest["id"] = request.SelfUrl;

        // Build service descriptors using Presentation 3 id/type conventions.
        // v2 is listed first; v1 follows for backward-compatible clients.
        var base_ = request.SearchBaseUrl;
        var id = request.Id;

        var searchServiceV2 = new JsonObject
        {
            ["id"] = $"{base_}/search/v2/{id}",
            ["type"] = "SearchService2",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"] = $"{base_}/autocomplete/v2/{id}",
                ["type"] = "AutoCompleteService2",
            }),
        };

        var searchServiceV1 = new JsonObject
        {
            ["id"] = $"{base_}/search/v1/{id}",
            ["type"] = "SearchService1",
            ["profile"] = "http://iiif.io/api/search/1/search",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"] = $"{base_}/autocomplete/v1/{id}",
                ["type"] = "AutoCompleteService1",
                ["profile"] = "http://iiif.io/api/search/1/autocomplete",
            }),
        };

        // Insert v2 then v1 at position 0 so v2 appears first.
        // Existing services (e.g. IIIF Image services) are pushed down, not displaced.
        if (manifest["service"] is JsonArray existingArray)
        {
            existingArray.Insert(0, searchServiceV1);
            existingArray.Insert(0, searchServiceV2);
        }
        else if (manifest["service"] is JsonObject existingObject)
        {
            // Spec allows service to be a single object — promote to array.
            manifest["service"] = new JsonArray(
                searchServiceV2, searchServiceV1, existingObject.DeepClone());
        }
        else
        {
            manifest["service"] = new JsonArray(searchServiceV2, searchServiceV1);
        }

        // ---- rendering links (PDF + plain text) ---------------------------------
        // Plain text is always added when text artefacts exist.
        // PDF is only added when at least one canvas is image-based (IsTemporalContent == false);
        // temporal-only manifests (VTT/audio/video) have no page images to render into a PDF.
        var text = await textCache.GetTextAsync(request.Id, ct);
        if (text != null)
        {
            var hasImageCanvases = text.Images.Any(img => !img.IsTemporalContent);

            var textRef = new JsonObject
            {
                ["id"] = $"{base_}/text/v1/{id}",
                ["type"] = "Text",
                ["label"] = new JsonObject { ["en"] = new JsonArray("View as plain text") },
                ["format"] = "text/plain",
            };

            if (manifest["rendering"] is JsonArray existingRendering)
            {
                existingRendering.Insert(0, textRef);
                if (hasImageCanvases)
                {
                    var pdfRef = BuildPdfRef(base_, id);
                    existingRendering.Insert(0, pdfRef);
                }
            }
            else if (manifest["rendering"] is JsonObject singleRendering)
            {
                manifest["rendering"] = hasImageCanvases
                    ? new JsonArray(BuildPdfRef(base_, id), textRef, singleRendering.DeepClone())
                    : new JsonArray(textRef, singleRendering.DeepClone());
            }
            else
            {
                manifest["rendering"] = hasImageCanvases
                    ? new JsonArray(BuildPdfRef(base_, id), textRef)
                    : new JsonArray(textRef);
            }
        }

        // ---- per-canvas line and word annotation page references ----------------
        // Inject line-level and word-level annotation page links into each canvas's
        // annotations array so harvesting clients can discover and fetch them.
        // Only canvases that actually have words are decorated; sparse canvases are skipped.
        if (text != null && manifest["items"] is JsonArray canvases)
        {
            // Build a set of canvas indices that have at least one word, in O(words).
            var canvasesWithWords = text.Words.Values.Select(w => w.Idx).ToHashSet();

            for (var i = 0; i < canvases.Count && i < text.Images.Length; i++)
            {
                if (!canvasesWithWords.Contains(i)) continue;
                if (canvases[i] is not JsonObject canvas) continue;

                var linesRef = new JsonObject
                {
                    ["id"] = $"{base_}/annotations/lines/v1/{i}/{id}",
                    ["type"] = "AnnotationPage",
                    ["label"] = new JsonObject { ["en"] = new JsonArray("Line-level transcription") },
                };
                var wordsRef = new JsonObject
                {
                    ["id"] = $"{base_}/annotations/words/v1/{i}/{id}",
                    ["type"] = "AnnotationPage",
                    ["label"] = new JsonObject { ["en"] = new JsonArray("Word-level transcription") },
                };

                if (canvas["annotations"] is JsonArray existingAnnos)
                {
                    existingAnnos.Insert(0, wordsRef);
                    existingAnnos.Insert(0, linesRef);
                }
                else if (canvas["annotations"] is JsonObject singleAnno)
                {
                    canvas["annotations"] = new JsonArray(
                        linesRef, wordsRef, singleAnno.DeepClone());
                }
                else
                {
                    canvas["annotations"] = new JsonArray(linesRef, wordsRef);
                }
            }
        }

        // ---- manifest-level line annotations reference --------------------------
        // If the builder stored an annotations.json (all canvases, line granularity),
        // add a manifest-level annotations reference so clients can discover it.
        var annotationsUrl = $"{base_}/annotations/manifest/v1/{id}";
        var annotationsJson = await textStore.LoadAnnotations(request.Id);
        if (annotationsJson != null)
        {
            var annotationsRef = new JsonObject
            {
                ["id"] = annotationsUrl,
                ["type"] = "AnnotationPage",
                ["profile"] = "https://dlcs.io/profiles/all-text",
                ["label"] = new JsonObject { ["en"] = new JsonArray("Text of all canvases") },
            };

            if (manifest["annotations"] is JsonArray existingAnnos)
                existingAnnos.Insert(0, annotationsRef);
            else if (manifest["annotations"] is JsonObject singleAnno)
                manifest["annotations"] = new JsonArray(annotationsRef, singleAnno.DeepClone());
            else
                manifest["annotations"] = new JsonArray(annotationsRef);
        }

        // ---- figures annotation page reference ----------------------------------
        // If the builder stored a figures.json (ComposedBlocks with non-zero area),
        // add a manifest-level annotations reference so clients can discover it.
        var figuresUrl = $"{base_}/identified/figures/{id}";
        var figuresJson = await textStore.LoadFigures(request.Id);
        if (figuresJson != null)
        {
            var figuresRef = new JsonObject
            {
                ["id"] = figuresUrl,
                ["type"] = "AnnotationPage",
                ["label"] = new JsonObject
                {
                    ["en"] = new JsonArray("Figures, tables and illustrations"),
                },
            };

            if (manifest["annotations"] is JsonArray existingAnnos)
            {
                existingAnnos.Add(figuresRef);
            }
            else if (manifest["annotations"] is JsonObject singleAnno)
            {
                manifest["annotations"] = new JsonArray(singleAnno.DeepClone(), figuresRef);
            }
            else
            {
                manifest["annotations"] = new JsonArray(figuresRef);
            }
        }

        return manifest;
    }

    private static JsonObject BuildPdfRef(string baseUrl, string id) => new()
    {
        ["id"] = $"{baseUrl}/pdf/v1/{id}",
        ["type"] = "Text",
        ["label"] = new JsonObject { ["en"] = new JsonArray("Download as PDF") },
        ["format"] = "application/pdf",
    };
}
