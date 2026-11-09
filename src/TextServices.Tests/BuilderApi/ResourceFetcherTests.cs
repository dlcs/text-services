using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TextServices.Builder.Api.Services;
using TextServices.Infrastructure.Http.IpBlocking;

namespace TextServices.Tests.BuilderApi;

public class ResourceFetcherTests
{
    [Fact]
    public async Task FetchAsync_Throws_BlockedException_IfHostBlocked()
    {
        var blocked = new IpAddressBlockedException("internal.example.org", IPAddress.Loopback,
            IPNetwork.Parse("127.0.0.0/8"));

        // HttpClient wraps anything raised from ConnectCallback in an HttpRequestException
        var sut = GetSut(new ThrowingHandler(new HttpRequestException("wrapped", blocked)));

        var exception = await Should.ThrowAsync<IpAddressBlockedException>(
            () => sut.FetchAsync("https://internal.example.org/manifest"));

        exception.ShouldBeSameAs(blocked);
    }

    [Fact]
    public async Task FetchAsync_RethrowsOriginalException_IfNotBlocked()
    {
        var sut = GetSut(new ThrowingHandler(new HttpRequestException("connection refused")));

        var exception = await Should.ThrowAsync<HttpRequestException>(
            () => sut.FetchAsync("https://example.org/manifest"));

        exception.Message.ShouldBe("connection refused");
    }

    private static ResourceFetcher GetSut(HttpMessageHandler handler)
        => new(new StubHttpClientFactory(handler), null, NullLogger<ResourceFetcher>.Instance);

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw exception;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
