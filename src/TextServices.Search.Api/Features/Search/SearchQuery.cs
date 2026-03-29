using MediatR;
using TextServices.Core.Models;
using TextServices.Search.Api.Models;
using TextServices.Search.Api.Services;

namespace TextServices.Search.Api.Features.Search;

public record SearchRequest(string Id, string Query, string SelfUrl) : IRequest<SearchAnnotationList?>;

public class SearchHandler(ITextCache cache) : IRequestHandler<SearchRequest, SearchAnnotationList?>
{
    public async Task<SearchAnnotationList?> Handle(SearchRequest request, CancellationToken ct)
    {
        var text = await cache.GetTextAsync(request.Id, ct);
        if (text == null) return null;

        var rects = string.IsNullOrWhiteSpace(request.Query)
            ? []
            : text.Search(request.Query);

        return BuildResponse(text, rects, request.SelfUrl);
    }

    private static SearchAnnotationList BuildResponse(
        Text text, List<ResultRect> rects, string selfUrl)
    {
        var resources      = new List<SearchAnnotation>(rects.Count);
        var hits           = new List<SearchHit>();
        SearchHit?         currentHit      = null;
        int                currentHitIndex = -1;
        List<string>       hitAnnoIds      = [];
        string?            after           = null;

        foreach (var rect in rects)
        {
            var canvasId = text.Images[rect.Idx].ImageIdentifier;
            // Annotation ID encodes hit + canvas index + coordinates for uniqueness.
            var annoId   = $"{selfUrl}/anno/h{rect.Hit}i{rect.Idx}-{rect.X},{rect.Y},{rect.W},{rect.H}";

            resources.Add(new SearchAnnotation
            {
                Id       = annoId,
                Resource = new SearchAnnotationResource { Chars = rect.ContentRaw },
                On       = $"{canvasId}#xywh={rect.X},{rect.Y},{rect.W},{rect.H}",
            });

            if (currentHitIndex != rect.Hit)
            {
                if (currentHit != null)
                {
                    currentHit.After       = after;
                    currentHit.Annotations = [.. hitAnnoIds];
                    hits.Add(currentHit);
                }

                currentHit = new SearchHit
                {
                    Before      = rect.Before,
                    Match       = string.Empty,
                    Annotations = [], // filled when the hit is closed
                };
                currentHitIndex = rect.Hit;
                hitAnnoIds      = [];
            }

            currentHit!.Match += string.IsNullOrEmpty(currentHit.Match)
                ? rect.ContentRaw
                : $" {rect.ContentRaw}";

            after = rect.After;
            hitAnnoIds.Add(annoId);
        }

        // Close the final hit.
        if (currentHit != null)
        {
            currentHit.After       = after;
            currentHit.Annotations = [.. hitAnnoIds];
            hits.Add(currentHit);
        }

        return new SearchAnnotationList
        {
            Id        = selfUrl,
            Within    = new SearchLayer { Total = resources.Count },
            Resources = resources,
            Hits      = hits,
        };
    }
}
