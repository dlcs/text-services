using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Figures;

internal static class FiguresEndpoints
{
    internal static IEndpointRouteBuilder MapFiguresEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/identified/figures/{**id}", async (
            string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "identified/figures/", id);
            var result = await sender.Send(new FiguresRequest(id, resolved.SelfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
