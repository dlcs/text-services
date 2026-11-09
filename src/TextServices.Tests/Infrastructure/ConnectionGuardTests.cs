using System.Net;
using System.Net.Sockets;
using System.Text;
using Shouldly;
using TextServices.Infrastructure.Http.IpBlocking;

namespace TextServices.Tests.Infrastructure;

public class ConnectionGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1/foo")]
    [InlineData("http://127.1.2.3/foo")]
    [InlineData("https://127.0.0.1/foo")]
    [InlineData("http://[::1]/foo")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[fe80::1]/foo")]
    [InlineData("http://[fd00:ec2::254]/latest/meta-data")]
    [InlineData("http://localhost/foo")]
    public async Task ConnectAsync_Throws_IfHostIsDefaultBlocked(string uri)
    {
        using var httpClient = GetHttpClient();

        var exception = await Should.ThrowAsync<HttpRequestException>(() => httpClient.GetAsync(uri));

        exception.InnerException.ShouldBeOfType<IpAddressBlockedException>();
    }

    [Fact]
    public async Task ConnectAsync_Throws_IfHostInConfiguredRange()
    {
        using var httpClient = GetHttpClient(blocked: ["10.0.0.0/8"]);

        var exception = await Should.ThrowAsync<HttpRequestException>(
            () => httpClient.GetAsync("http://10.1.2.3/foo"));

        exception.InnerException.ShouldBeOfType<IpAddressBlockedException>();
    }

    [Fact]
    public async Task ConnectAsync_Connects_IfHostInAllowedRange()
    {
        using var server = new StubServer("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        using var httpClient = GetHttpClient(allowed: ["127.0.0.1/32"]);

        var response = await httpClient.GetStringAsync($"http://127.0.0.1:{server.Port}/");

        response.ShouldBe("ok");
    }

    [Fact]
    public async Task ConnectAsync_Throws_IfRedirectedToBlockedHost()
    {
        // The requested host is allowed, but it redirects to a blocked address
        using var server = new StubServer(
            "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.2/foo\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var httpClient = GetHttpClient(allowed: ["127.0.0.1/32"]);

        var exception = await Should.ThrowAsync<HttpRequestException>(
            () => httpClient.GetAsync($"http://127.0.0.1:{server.Port}/"));

        var blocked = exception.InnerException.ShouldBeOfType<IpAddressBlockedException>();
        blocked.Host.ShouldBe("127.0.0.2");
        blocked.Address.ShouldBe(IPAddress.Parse("127.0.0.2"));
        blocked.BlockedBy.ShouldBe(IPNetwork.Parse("127.0.0.0/8"));
    }

    [Fact]
    public void FindInChain_ReturnsBlockedException_IfWrapped()
    {
        var blocked = new IpAddressBlockedException("localhost", IPAddress.Loopback, IPNetwork.Parse("127.0.0.0/8"));
        var wrapped = new HttpRequestException("outer", new InvalidOperationException("middle", blocked));

        IpAddressBlockedException.FindInChain(wrapped).ShouldBeSameAs(blocked);
    }

    [Fact]
    public void FindInChain_ReturnsNull_IfNotInChain()
        => IpAddressBlockedException.FindInChain(new HttpRequestException("outer")).ShouldBeNull();

    private static HttpClient GetHttpClient(string[]? blocked = null, string[]? allowed = null)
    {
        var guard = new ConnectionGuard(new IpAddressPolicy(
            new IpBlockingSettings { BlockedIpRanges = blocked ?? [], AllowedIpRanges = allowed ?? [] }));

        // Exercise the guard as it's wired up in DI, so that HttpClient behaviour is included
        // UseProxy:false as a proxy would connect us to the proxy address, rather than the requested host
        var handler = new SocketsHttpHandler { ConnectCallback = guard.ConnectAsync, UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>Minimal loopback TCP server that replies to every connection with a fixed HTTP response.</summary>
    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cts = new();

        public StubServer(string response)
        {
            listener.Start();
            _ = AcceptLoop(Encoding.ASCII.GetBytes(response));
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        private async Task AcceptLoop(byte[] response)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    using var socket = await listener.AcceptSocketAsync(cts.Token);
                    var buffer = new byte[4096];
                    await socket.ReceiveAsync(buffer, cts.Token);
                    await socket.SendAsync(response, cts.Token);
                    socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            listener.Stop();
        }
    }
}
