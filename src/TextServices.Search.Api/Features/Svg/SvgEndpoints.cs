using MediatR;

namespace TextServices.Search.Api.Features.Svg;

internal static class SvgEndpoints
{
    internal static IEndpointRouteBuilder MapSvgEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/svg/v1/{n:int}/{*id:minlength(1)}", async (
            int n, string id,
            ISender sender) =>
        {
            var result = await sender.Send(new SvgRequest(id, n));
            if (result == null) return Results.NotFound();
            return Results.Content(result, "image/svg+xml");
        });

        return routes;
    }
}
