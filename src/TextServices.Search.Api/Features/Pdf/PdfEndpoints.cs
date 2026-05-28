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
            var result = await sender.Send(new PdfTriggerRequest(id));
            return result switch
            {
                PdfTriggerResult.AlreadyExists => Results.Ok(new
                {
                    location = EndpointHelpers.BuildSelfUrl(options.Value, ctx, $"pdf/v1/{id}", null)
                }),
                PdfTriggerResult.Queued => Results.Accepted(
                    EndpointHelpers.BuildSelfUrl(options.Value, ctx, $"pdf/v1/{id}", null)),
                PdfTriggerResult.ServiceBusy => new ServiceBusyResult(),
                _ => Results.NotFound(),
            };
        });

        return routes;
    }

    private static string StripPdfExtension(string id) =>
        id.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
}

file sealed class ServiceBusyResult : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "30";
        return Task.CompletedTask;
    }
}
