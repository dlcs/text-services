using TextServices.Core.Models;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Search;

public abstract class SearchHandlerBase<TResponse>(ITextCache cache)
{
    protected async Task<TResponse?> HandleCore(string id, string query, string selfUrl, string resourceUrl, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(id, JobServices.Search, ct)) return default;

        if (string.IsNullOrWhiteSpace(query))
            return EmptyQueryResponse(selfUrl, resourceUrl);

        var text = await cache.GetTextAsync(id, ct);
        if (text == null) return default;

        return BuildResponse(text, text.Search(query), selfUrl, resourceUrl);
    }

    protected abstract TResponse EmptyQueryResponse(string selfUrl, string resourceUrl);
    protected abstract TResponse BuildResponse(Text text, List<ResultRect> rects, string selfUrl, string resourceUrl);
}
