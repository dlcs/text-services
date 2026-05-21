using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Pdf;

internal static class PdfEndpoints
{
    internal static IEndpointRouteBuilder MapPdfEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/pdf/v1/{**id}", async (string id, ISender sender) =>
        {
            id = StripPdfExtension(id);
            var stream = await sender.Send(new PdfRequest(id));
            if (stream == null) return Results.NotFound();
            return Results.Stream(stream, "application/pdf", enableRangeProcessing: false);
        });

        routes.MapPost("/pdf/v1/{**id}", async (
            string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            id = StripPdfExtension(id);
            var started = await sender.Send(new PdfTriggerRequest(id));
            if (!started)
            {
                var location = EndpointHelpers.BuildSelfUrl(options.Value, ctx, $"pdf/v1/{id}", null);
                return Results.Ok(new { location });
            }
            var locationUrl = EndpointHelpers.BuildSelfUrl(options.Value, ctx, $"pdf/v1/{id}", null);
            return Results.Accepted(locationUrl);
        });

        return routes;
    }

    private static string StripPdfExtension(string id) =>
        id.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
}
