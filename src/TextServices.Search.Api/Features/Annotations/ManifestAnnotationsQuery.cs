using System.Text.Json.Nodes;
using MediatR;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Annotations;

/// <summary>
/// Returns the stored manifest-level line-annotation <c>AnnotationPage</c> for the
/// given job ID, with <c>id</c> and per-annotation <c>id</c> values set to the
/// request URL.
/// </summary>
public record ManifestAnnotationsRequest(string Id, string SelfUrl) : IRequest<JsonNode?>;

public class ManifestAnnotationsHandler(ITextStore textStore)
    : IRequestHandler<ManifestAnnotationsRequest, JsonNode?>
{
    public async Task<JsonNode?> Handle(ManifestAnnotationsRequest request, CancellationToken ct)
    {
        var json = await textStore.LoadAnnotations(request.Id);
        if (json == null) return null;

        var node = JsonNode.Parse(json);
        if (node is not JsonObject page) return null;

        page["id"] = request.SelfUrl;

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
