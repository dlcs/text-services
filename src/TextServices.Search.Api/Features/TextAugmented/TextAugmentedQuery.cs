using System.Text.Json.Nodes;
using MediatR;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.TextAugmented;

/// <summary>
/// Returns the stored IIIF Manifest JSON decorated with IIIF Search v1 service descriptors.
/// The manifest is treated as plain JSON — no IIIF object model required.
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

        // Build the search and autocomplete service descriptors.
        var searchUrl  = $"{request.SearchBaseUrl}/search/v1/{request.Id}";
        var autocompleteUrl = $"{request.SearchBaseUrl}/autocomplete/v1/{request.Id}";

        var searchService = new JsonObject
        {
            ["@context"] = "http://iiif.io/api/search/1/context.json",
            ["@id"]      = searchUrl,
            ["profile"]  = "http://iiif.io/api/search/1/search",
            ["label"]    = "Search within this manifest",
            ["service"]  = new JsonObject
            {
                ["@context"] = "http://iiif.io/api/search/1/context.json",
                ["@id"]      = autocompleteUrl,
                ["profile"]  = "http://iiif.io/api/search/1/autocomplete"
            }
        };

        // Insert at position 0 so IIIF clients that take the first search service they find
        // will use ours. Existing services are pushed down the array, not displaced.
        if (manifest["service"] is JsonArray existingArray)
        {
            existingArray.Insert(0, searchService);
        }
        else if (manifest["service"] is JsonObject existingObject)
        {
            // Spec allows service to be a single object — promote to array with ours first.
            manifest["service"] = new JsonArray(searchService, existingObject.DeepClone());
        }
        else
        {
            manifest["service"] = new JsonArray(searchService);
        }

        return manifest;
    }
}
