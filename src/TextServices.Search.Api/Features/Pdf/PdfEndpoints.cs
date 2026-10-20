using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Pdf;

internal static class PdfEndpoints
{
    private const string RoutePrefix = "rendering/pdf/v1/";

    // Legacy prefix kept so existing CloudFront behaviours for /pdf/v1/* keep working.
    // Remove once those behaviours are moved to /rendering/pdf/v1/*
    private const string LegacyRoutePrefix = "pdf/v1/";

    internal static IEndpointRouteBuilder MapPdfEndpoints(this IEndpointRouteBuilder routes)
    {
        MapPdfRoutes(routes, RoutePrefix);
        MapPdfRoutes(routes, LegacyRoutePrefix);
        return routes;
    }

    private static void MapPdfRoutes(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"/{prefix}{{*id:minlength(1)}}", async (string id, ISender sender) =>
        {
            id = StripPdfExtension(id);
            var stream = await sender.Send(new PdfRequest(id));
            if (stream == null) return Results.NotFound();
            return Results.Stream(stream, "application/pdf", enableRangeProcessing: false);
        });

        // The self URL uses the prefix of the route that was hit, so the Location returned to a
        // caller on the legacy path stays reachable via the same proxy behaviour.
        routes.MapPost($"/{prefix}{{*id:minlength(1)}}", async (
            string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            id = StripPdfExtension(id);
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, prefix, id);
            var result = await sender.Send(new PdfTriggerRequest(id));
            return result switch
            {
                PdfTriggerResult.AlreadyExists => Results.Ok(new { location = resolved.SelfUrl }),
                PdfTriggerResult.Queued => Results.Accepted(resolved.SelfUrl),
                PdfTriggerResult.ServiceBusy => new ServiceBusyResult(),
                _ => Results.NotFound(),
            };
        });
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
