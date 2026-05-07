using Microsoft.AspNetCore.Http;

namespace TextServices.Infrastructure.Http;

/// <summary>
/// Ensures there is an x-correlation-id value in the response. Add early in the pipeline
/// so it is available to downstream middleware and Serilog enrichment.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationHeaderKey = "x-correlation-id";

    public async Task InvokeAsync(HttpContext context)
    {
        var headerValue = context.TryGetHeaderValue(CorrelationHeaderKey, checkResponse: false)
                          ?? Guid.NewGuid().ToString();

        if (!context.Response.HasStarted && !context.Response.Headers.ContainsKey(CorrelationHeaderKey))
            context.Response.Headers[CorrelationHeaderKey] = headerValue;

        await next(context);
    }
}
