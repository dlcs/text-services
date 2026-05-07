using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Primitives;

namespace TextServices.Infrastructure.Http;

public static class CorrelationIdServiceCollectionExtensions
{
    /// <summary>
    /// Propagates x-correlation-id to all outgoing HttpClient requests.
    /// </summary>
    public static IServiceCollection AddCorrelationIdHeaderPropagation(this IServiceCollection services)
    {
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, HeaderPropagationMessageHandlerBuilderFilter>();
        return services;
    }
}

internal class HeaderPropagationMessageHandlerBuilderFilter(IHttpContextAccessor contextAccessor)
    : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            builder.AdditionalHandlers.Add(new PropagateCorrelationIdHandler(contextAccessor));
            next(builder);
        };
    }
}

public class PropagateCorrelationIdHandler(IHttpContextAccessor contextAccessor) : DelegatingHandler
{
    private const string CorrelationHeaderKey = "x-correlation-id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (contextAccessor.HttpContext == null)
            return base.SendAsync(request, cancellationToken);

        var headerValue = contextAccessor.HttpContext.TryGetHeaderValue(CorrelationHeaderKey);
        if (!string.IsNullOrEmpty(headerValue))
            request.Headers.TryAddWithoutValidation(CorrelationHeaderKey, [headerValue]);

        return base.SendAsync(request, cancellationToken);
    }
}

internal static class HttpContextExtensions
{
    public static string? TryGetHeaderValue(this HttpContext? httpContext, string headerKey, bool checkResponse = true)
    {
        if (httpContext == null) return null;

        if (TryGetValue(httpContext.Request.Headers, headerKey, out var fromRequest))
            return fromRequest;

        if (checkResponse && TryGetValue(httpContext.Response.Headers, headerKey, out var fromResponse))
            return fromResponse;

        return null;
    }

    private static bool TryGetValue(IHeaderDictionary headers, string headerKey, out string? value)
    {
        value = null;
        if (headers.TryGetValue(headerKey, out var values))
        {
            value = values.FirstOrDefault();
            return !StringValues.IsNullOrEmpty(value);
        }
        return false;
    }
}
