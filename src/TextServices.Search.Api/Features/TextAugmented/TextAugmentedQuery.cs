using System.Text.Json.Nodes;
using MediatR;
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

public class TextAugmentedHandler(ITextStore textStore)
    : IRequestHandler<TextAugmentedRequest, JsonNode?>
{
    public async Task<JsonNode?> Handle(TextAugmentedRequest request, CancellationToken ct)
    {
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
        var id    = request.Id;

        var searchServiceV2 = new JsonObject
        {
            ["id"]   = $"{base_}/search/v2/{id}",
            ["type"] = "SearchService2",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"]   = $"{base_}/autocomplete/v2/{id}",
                ["type"] = "AutoCompleteService2",
            }),
        };

        var searchServiceV1 = new JsonObject
        {
            ["id"]   = $"{base_}/search/v1/{id}",
            ["type"] = "SearchService1",
            ["service"] = new JsonArray(new JsonObject
            {
                ["id"]   = $"{base_}/autocomplete/v1/{id}",
                ["type"] = "AutoCompleteService1",
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

        // ---- figures annotation page reference ----------------------------------
        // If the builder stored a figures.json (ComposedBlocks with non-zero area),
        // add a manifest-level annotations reference so clients can discover it.
        var figuresUrl  = $"{base_}/identified/figures/{id}";
        var figuresJson = await textStore.LoadFigures(request.Id);
        if (figuresJson != null)
        {
            var figuresRef = new JsonObject
            {
                ["id"]   = figuresUrl,
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
}
