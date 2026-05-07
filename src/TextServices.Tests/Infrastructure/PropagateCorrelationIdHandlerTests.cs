using Microsoft.AspNetCore.Http;
using Shouldly;
using TextServices.Infrastructure.Http;

namespace TextServices.Tests.Infrastructure;

public class PropagateCorrelationIdHandlerTests
{
    [Fact]
    public async Task SendAsync_WithCorrelationIdInRequestHeader_ForwardsToOutgoingRequest()
    {
        HttpRequestMessage? captured = null;
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-correlation-id"] = "test-id";

        await Send(httpContext, req => captured = req);

        captured!.Headers.TryGetValues("x-correlation-id", out var values).ShouldBeTrue();
        values!.Single().ShouldBe("test-id");
    }

    [Fact]
    public async Task SendAsync_WithNoHttpContext_DoesNotAddHeader()
    {
        HttpRequestMessage? captured = null;

        await Send(null, req => captured = req);

        captured!.Headers.Contains("x-correlation-id").ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_WithNoCorrelationIdHeader_DoesNotAddHeader()
    {
        HttpRequestMessage? captured = null;
        var httpContext = new DefaultHttpContext();

        await Send(httpContext, req => captured = req);

        captured!.Headers.Contains("x-correlation-id").ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_WithCorrelationIdOnResponse_ForwardsToOutgoingRequest()
    {
        // Middleware sets the ID on the response when there is no incoming header;
        // downstream calls made after that should still pick it up.
        HttpRequestMessage? captured = null;
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Headers["x-correlation-id"] = "from-response";

        await Send(httpContext, req => captured = req);

        captured!.Headers.TryGetValues("x-correlation-id", out var values).ShouldBeTrue();
        values!.Single().ShouldBe("from-response");
    }

    // -------------------------------------------------------------------------

    private static Task Send(HttpContext? httpContext, Action<HttpRequestMessage> onSend)
    {
        var accessor = new StubHttpContextAccessor(httpContext);
        var handler = new PropagateCorrelationIdHandler(accessor)
        {
            InnerHandler = new CaptureHandler(onSend)
        };
        var invoker = new HttpMessageInvoker(handler);
        return invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com"),
            CancellationToken.None);
    }

    private sealed class StubHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class CaptureHandler(Action<HttpRequestMessage> onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
