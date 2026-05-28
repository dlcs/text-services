using MediatR;
using TextServices.Core.Models;
using TextServices.Search.Api.Models;
using TextServices.Search.Api.Services;

namespace TextServices.Search.Api.Features.Search;

public record SearchRequest(string Id, string Query, string SelfUrl, string ResourceUrl) : IRequest<SearchAnnotationList?>;

public class SearchHandler(ITextCache cache)
    : SearchHandlerBase<SearchAnnotationList>(cache), IRequestHandler<SearchRequest, SearchAnnotationList?>
{
    public Task<SearchAnnotationList?> Handle(SearchRequest request, CancellationToken ct)
        => HandleCore(request.Id, request.Query, request.SelfUrl, request.ResourceUrl, ct);

    protected override SearchAnnotationList EmptyQueryResponse(string selfUrl, string resourceUrl) =>
        new() { Id = selfUrl, Within = new SearchLayer { Total = 0 }, Resources = [], Hits = [] };

    protected override SearchAnnotationList BuildResponse(Text text, List<ResultRect> rects, string selfUrl, string resourceUrl)
    {
        var resources = new List<SearchAnnotation>(rects.Count);
        var hits = new List<SearchHit>();
        SearchHit? currentHit = null;
        int currentHitIndex = -1;
        List<string> hitAnnoIds = [];
        string? after = null;

        foreach (var rect in rects)
        {
            var canvasId = text.Images[rect.Idx].ImageIdentifier;
            // Annotation ID encodes hit + canvas index + coordinates for uniqueness.
            var annoId = $"{resourceUrl}/anno/h{rect.Hit}i{rect.Idx}-{rect.X},{rect.Y},{rect.W},{rect.H}";

            resources.Add(new SearchAnnotation
            {
                Id = annoId,
                Resource = new SearchAnnotationResource { Chars = rect.ContentRaw },
                On = $"{canvasId}#xywh={rect.X},{rect.Y},{rect.W},{rect.H}",
            });

            if (currentHitIndex != rect.Hit)
            {
                if (currentHit != null)
                {
                    currentHit.After = after;
                    currentHit.Annotations = [.. hitAnnoIds];
                    hits.Add(currentHit);
                }

                currentHit = new SearchHit
                {
                    Before = rect.Before,
                    Match = string.Empty,
                    Annotations = [],
                };
                currentHitIndex = rect.Hit;
                hitAnnoIds = [];
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
            currentHit.After = after;
            currentHit.Annotations = [.. hitAnnoIds];
            hits.Add(currentHit);
        }

        return new SearchAnnotationList
        {
            Id = selfUrl,
            Within = new SearchLayer { Total = resources.Count },
            Resources = resources,
            Hits = hits,
        };
    }
}
