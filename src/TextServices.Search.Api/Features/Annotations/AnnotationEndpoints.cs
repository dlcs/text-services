using MediatR;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Annotations;

internal static class AnnotationEndpoints
{
    internal static IEndpointRouteBuilder MapAnnotationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/annotations/manifest/v1/{**id}", async (
            string id,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"annotations/manifest/v1/{id}", null);
            var result = await sender.Send(new ManifestAnnotationsRequest(id, selfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/annotations/lines/v1/{n:int}/{**id}", async (
            int n, string id,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"annotations/lines/v1/{n}/{id}", null);
            var result = await sender.Send(new LineAnnotationsRequest(id, n, selfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/annotations/words/v1/{n:int}/{**id}", async (
            int n, string id,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"annotations/words/v1/{n}/{id}", null);
            var result = await sender.Send(new WordAnnotationsRequest(id, n, selfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
