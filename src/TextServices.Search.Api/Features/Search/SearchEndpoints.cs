using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Search;

internal static class SearchEndpoints
{
    internal static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/search/v1/{**id}", async (
            string id, string? q,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "search/v1/", id, q);
            var result = await sender.Send(new SearchRequest(id, q ?? string.Empty, resolved.SelfUrl, resolved.ResourceUrl));
            if (result == null) return Results.NotFound();
            result.Ignored = EndpointHelpers.GetIgnoredParams(ctx);
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/search/v2/{**id}", async (
            string id, string? q,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "search/v2/", id, q);
            var result = await sender.Send(new SearchV2Request(id, q ?? string.Empty, resolved.SelfUrl, resolved.ResourceUrl));
            if (result == null) return Results.NotFound();
            result.Ignored = EndpointHelpers.GetIgnoredParams(ctx);
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
