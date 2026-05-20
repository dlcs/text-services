using MediatR;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.TextAugmented;

internal static class TextAugmentedEndpoints
{
    internal static IEndpointRouteBuilder MapTextAugmentedEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/text-augmented/v3/{**id}", async (
            string id,
            ISender sender,
            SearchApiOptions options,
            HttpContext ctx) =>
        {
            var selfUrl = EndpointHelpers.BuildSelfUrl(options, ctx, $"text-augmented/v3/{id}", null);
            var searchBase = string.IsNullOrEmpty(options.BaseUrl)
                ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
                : options.BaseUrl.TrimEnd('/');

            var result = await sender.Send(new TextAugmentedRequest(id, selfUrl, searchBase));
            if (result == null) return Results.NotFound();
            return Results.Json(result, contentType: "application/ld+json");
        });

        return routes;
    }
}
