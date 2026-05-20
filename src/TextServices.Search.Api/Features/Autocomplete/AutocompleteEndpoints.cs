using MediatR;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Autocomplete;

internal static class AutocompleteEndpoints
{
    internal static IEndpointRouteBuilder MapAutocompleteEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/autocomplete/v1/{**id}", async (
            string id, string? q,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"autocomplete/v1/{id}", q);
            var result = await sender.Send(new AutocompleteRequest(id, q ?? string.Empty, selfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/autocomplete/v2/{**id}", async (
            string id, string? q,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"autocomplete/v2/{id}", q);
            var result = await sender.Send(new AutocompleteV2Request(id, q ?? string.Empty, selfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
