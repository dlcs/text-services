using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Autocomplete;

internal static class AutocompleteEndpoints
{
    internal static IEndpointRouteBuilder MapAutocompleteEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/autocomplete/v1/{*id:minlength(1)}", async (
            string id, string? q,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest();

            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "autocomplete/v1/", id, q);
            var result = await sender.Send(new AutocompleteRequest(id, q, resolved.SelfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/autocomplete/v2/{*id:minlength(1)}", async (
            string id, string? q,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest();

            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "autocomplete/v2/", id, q);
            var result = await sender.Send(new AutocompleteV2Request(id, q, resolved.SelfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
