using System.Text.Json.Nodes;
using MediatR;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Annotations;

/// <summary>
/// Returns a IIIF Presentation 3 <c>AnnotationPage</c> with one annotation per word
/// for a single canvas, using the IIIF Text Granularity extension
/// (<c>textGranularity: "word"</c>).
/// </summary>
public record WordAnnotationsRequest(string Id, int CanvasIndex, string SelfUrl)
    : IRequest<JsonObject?>;

public class WordAnnotationsHandler(ITextCache cache)
    : IRequestHandler<WordAnnotationsRequest, JsonObject?>
{
    public async Task<JsonObject?> Handle(WordAnnotationsRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Annotations, ct)) return null;
        var text = await cache.GetTextAsync(request.Id, ct);
        if (text == null) return null;

        if (request.CanvasIndex < 0 || request.CanvasIndex >= text.Images.Length)
            return null;

        return AnnotationPageBuilder.Build(text, request.CanvasIndex, request.SelfUrl,
            wordLevel: true);
    }
}
