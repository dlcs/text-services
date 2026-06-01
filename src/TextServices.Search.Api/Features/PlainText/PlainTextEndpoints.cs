using MediatR;

namespace TextServices.Search.Api.Features.PlainText;

internal static class PlainTextEndpoints
{
    internal static IEndpointRouteBuilder MapPlainTextEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/text/v1/{*id:minlength(1)}", async (string id, ISender sender) =>
        {
            var result = await sender.Send(new RawTextRequest(id));
            if (result == null) return Results.NotFound();
            return Results.Text(result, "text/plain");
        });

        return routes;
    }
}
