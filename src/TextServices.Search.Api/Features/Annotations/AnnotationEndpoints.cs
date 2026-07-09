using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Annotations;

internal static class AnnotationEndpoints
{
    internal static IEndpointRouteBuilder MapAnnotationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/annotations/manifest/v1/{*id:minlength(1)}", async (
            string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, "annotations/manifest/v1/", id);
            var result = await sender.Send(new ManifestAnnotationsRequest(id, resolved.SelfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/annotations/lines/v1/{n:int}/{*id:minlength(1)}", async (
            int n, string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, $"annotations/lines/v1/{n}/", id);
            var result = await sender.Send(new LineAnnotationsRequest(id, n, resolved.SelfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        routes.MapGet("/annotations/words/v1/{n:int}/{*id:minlength(1)}", async (
            int n, string id,
            ISender sender,
            IOptions<SearchApiOptions> options,
            HttpContext ctx) =>
        {
            var resolved = EndpointHelpers.Resolve(options.Value, ctx, $"annotations/words/v1/{n}/", id);
            var result = await sender.Send(new WordAnnotationsRequest(id, n, resolved.SelfUrl));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
