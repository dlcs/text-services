using System.Text.Json.Nodes;
using MediatR;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Figures;

/// <summary>
/// Returns the IIIF AnnotationPage of identified figures (ComposedBlocks extracted
/// from ALTO) for the given job ID, with the <c>id</c> and per-annotation <c>id</c>
/// values set to the request URL.
/// </summary>
public record FiguresRequest(string Id, string SelfUrl) : IRequest<JsonNode?>;

public class FiguresHandler(ITextStore textStore, ITextCache cache)
    : IRequestHandler<FiguresRequest, JsonNode?>
{
    public async Task<JsonNode?> Handle(FiguresRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Figures, ct)) return null;
        var json = await textStore.LoadFigures(request.Id);
        if (json == null) return null;

        var node = JsonNode.Parse(json);
        if (node is not JsonObject page) return null;

        // Inject the correct self URL.
        page["id"] = request.SelfUrl;

        // Prefix each annotation id with the self URL so they are absolute URIs.
        if (page["items"] is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is JsonObject anno)
                {
                    var relId = anno["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(relId))
                        anno["id"] = $"{request.SelfUrl}/{relId}";
                }
            }
        }

        return page;
    }
}
