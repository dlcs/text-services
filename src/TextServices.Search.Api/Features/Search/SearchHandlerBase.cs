using TextServices.Core.Models;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Search;

public abstract class SearchHandlerBase<TResponse>(ITextCache cache, ILogger logger)
{
    protected async Task<TResponse?> HandleCore(string id, string query, string selfUrl, string resourceUrl, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(id, JobServices.Search, ct))
        {
            logger.LogDebug("Search is disabled: {Id}", id);
            return default;
        }

        if (string.IsNullOrWhiteSpace(query)) return EmptyQueryResponse(selfUrl);

        var text = await cache.GetTextAsync(id, ct);
        if (text == null)
        {
            logger.LogInformation("No text found: {Id}", id);
            return default;
        }

        logger.LogDebug("Searching {Id} for {Query}", id, query);

        return BuildResponse(text, text.Search(query), selfUrl, resourceUrl);
    }

    protected abstract TResponse EmptyQueryResponse(string selfUrl);
    protected abstract TResponse BuildResponse(Text text, List<ResultRect> rects, string selfUrl, string resourceUrl);
}
