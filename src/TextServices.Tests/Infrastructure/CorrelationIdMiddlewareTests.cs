using Microsoft.AspNetCore.Http;
using Shouldly;
using TextServices.Infrastructure.Http;

namespace TextServices.Tests.Infrastructure;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithCorrelationIdInRequest_PassesThroughToResponse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-correlation-id"] = "my-correlation-id";

        await Invoke(context);

        context.Response.Headers["x-correlation-id"].ToString().ShouldBe("my-correlation-id");
    }

    [Fact]
    public async Task InvokeAsync_WithNoCorrelationId_GeneratesGuidOnResponse()
    {
        var context = new DefaultHttpContext();

        await Invoke(context);

        var header = context.Response.Headers["x-correlation-id"].ToString();
        header.ShouldNotBeEmpty();
        Guid.TryParse(header, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithHeaderAlreadyOnResponse_DoesNotOverwrite()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers["x-correlation-id"] = "already-set";

        await Invoke(context);

        context.Response.Headers["x-correlation-id"].ToString().ShouldBe("already-set");
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;

        var middleware = new CorrelationIdMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    private static Task Invoke(HttpContext context)
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        return middleware.InvokeAsync(context);
    }
}
