using MediatR;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Figures;

internal static class FiguresEndpoints
{
    internal static IEndpointRouteBuilder MapFiguresEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/identified/figures/{**id}", async (
            string id,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"identified/figures/{id}", null);
            var result = await sender.Send(new FiguresRequest(id, selfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
