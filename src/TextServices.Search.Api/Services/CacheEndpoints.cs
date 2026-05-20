namespace TextServices.Search.Api.Services;

internal static class CacheEndpoints
{
    internal static IEndpointRouteBuilder MapCacheEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/cache/v1/{**id}", (string id, ITextCache textCache) =>
        {
            textCache.Invalidate(id);
            return Results.NoContent();
        });

        return routes;
    }
}
