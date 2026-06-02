using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.TextAugmented;

internal static class TextAugmentedEndpoints
{
    internal static IEndpointRouteBuilder MapTextAugmentedEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/text-augmented/v3/{*id:minlength(1)}", async (
            string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "text-augmented/v3/", id);
            var result = await sender.Send(new TextAugmentedRequest(id, resolved));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
