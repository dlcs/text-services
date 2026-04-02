using System.Globalization;
using MediatR;
using TextServices.Core.Models;
using TextServices.Search.Api.Models;
using TextServices.Search.Api.Services;

namespace TextServices.Search.Api.Features.Search;

public record SearchV2Request(string Id, string Query, string SelfUrl) : IRequest<SearchAnnotationPageV2?>;

public class SearchV2Handler(ITextCache cache) : IRequestHandler<SearchV2Request, SearchAnnotationPageV2?>
{
    public async Task<SearchAnnotationPageV2?> Handle(SearchV2Request request, CancellationToken ct)
    {
        var text = await cache.GetTextAsync(request.Id, ct);
        if (text == null) return null;

        var rects = string.IsNullOrWhiteSpace(request.Query)
            ? []
            : text.Search(request.Query);

        return BuildResponse(text, rects, request.SelfUrl);
    }

    private static SearchAnnotationPageV2 BuildResponse(
        Text text, List<ResultRect> rects, string selfUrl)
    {
        var items    = new List<PaintingAnnotationV2>(rects.Count);
        var contexts = new List<ContextualizingAnnotation>();

        int     currentHitIndex = -1;
        string  hitMatch        = string.Empty;
        string? hitBefore       = null;
        string? hitAfter        = null;
        string? firstAnnoId     = null;

        foreach (var rect in rects)
        {
            var image    = text.Images[rect.Idx];
            var canvasId = image.ImageIdentifier;
            var isTemporal = image.IsTemporalContent;

            string annoId;
            string target;
            string motivation;

            if (isTemporal)
            {
                annoId     = $"{selfUrl}/anno/h{rect.Hit}i{rect.Idx}-t{rect.StartMs},{rect.EndMs}";
                target     = $"{canvasId}#{BuildTemporalTarget(rect.StartMs, rect.EndMs)}";
                motivation = "supplementing";
            }
            else
            {
                annoId     = $"{selfUrl}/anno/h{rect.Hit}i{rect.Idx}-{rect.X},{rect.Y},{rect.W},{rect.H}";
                target     = $"{canvasId}#xywh={rect.X},{rect.Y},{rect.W},{rect.H}";
                motivation = "painting";
            }

            items.Add(new PaintingAnnotationV2
            {
                Id         = annoId,
                Body       = new TextualBodyV2 { Value = rect.ContentRaw },
                Target     = target,
                Motivation = motivation,
            });

            if (currentHitIndex != rect.Hit)
            {
                // Close the previous hit group.
                if (currentHitIndex != -1 && firstAnnoId != null)
                {
                    contexts.Add(MakeContextualizing(
                        $"{selfUrl}/context/h{currentHitIndex}",
                        firstAnnoId, hitMatch, hitBefore, hitAfter));
                }

                currentHitIndex = rect.Hit;
                hitBefore       = rect.Before;
                hitMatch        = string.Empty;
                firstAnnoId     = annoId;
            }

            hitMatch += string.IsNullOrEmpty(hitMatch) ? rect.ContentRaw : $" {rect.ContentRaw}";
            hitAfter = rect.After;
        }

        // Close the final hit group.
        if (currentHitIndex != -1 && firstAnnoId != null)
        {
            contexts.Add(MakeContextualizing(
                $"{selfUrl}/context/h{currentHitIndex}",
                firstAnnoId, hitMatch, hitBefore, hitAfter));
        }

        return new SearchAnnotationPageV2
        {
            Id          = selfUrl,
            Items       = items,
            Annotations = contexts.Count > 0
                ? [new ContextualizingAnnotationPage { Items = contexts }]
                : null,
        };
    }

    private static string BuildTemporalTarget(int startMs, int endMs)
    {
        var start = (startMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        var end   = (endMs   / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        return $"t={start},{end}";
    }

    private static ContextualizingAnnotation MakeContextualizing(
        string id, string firstAnnoId, string match, string? before, string? after)
    {
        return new ContextualizingAnnotation
        {
            Id     = id,
            Target = new SpecificResourceTarget
            {
                Source   = firstAnnoId,
                Selector =
                [
                    new TextQuoteSelector
                    {
                        Exact  = match,
                        Prefix = string.IsNullOrEmpty(before) ? null : before,
                        Suffix = string.IsNullOrEmpty(after)  ? null : after,
                    }
                ],
            },
        };
    }
}
