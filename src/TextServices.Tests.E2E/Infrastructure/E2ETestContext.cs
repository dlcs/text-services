namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// IClassFixture shared across all E2E tests. Creates a single temp storage directory,
/// starts both API factories once, and tears them down after the test class completes.
/// </summary>
public sealed class E2ETestContext : IDisposable
{
    private static readonly string FixturesRoot = Path.Combine(
        AppContext.BaseDirectory, "Fixtures");

    public string StorageRoot { get; } =
        Path.Combine(Path.GetTempPath(), "TextServicesE2E_" + Guid.NewGuid().ToString("N"));

    public BuilderApiFactory BuilderFactory { get; }
    public SearchApiFactory SearchFactory { get; }

    public HttpClient BuilderClient { get; }
    public HttpClient SearchClient { get; }

    public E2ETestContext()
    {
        Directory.CreateDirectory(StorageRoot);

        BuilderFactory = new BuilderApiFactory(StorageRoot, FixturesRoot);
        SearchFactory = new SearchApiFactory(StorageRoot);

        BuilderClient = BuilderFactory.CreateClient();
        SearchClient = SearchFactory.CreateClient();
    }

    /// <summary>
    /// Polls GET /textbuilder/{id} until status is "Completed" or "Failed",
    /// or until <paramref name="timeout"/> elapses.
    /// Returns the final job response body.
    /// </summary>
    public async Task<string> WaitForJobAsync(
        string jobId,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        var delay = TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            var response = await BuilderClient.GetAsync($"/textbuilder/{jobId}", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (body.Contains("\"Completed\"") || body.Contains("\"Failed\""))
                return body;

            await Task.Delay(delay, ct);
            if (delay < TimeSpan.FromSeconds(2))
                delay += TimeSpan.FromMilliseconds(200);
        }

        throw new TimeoutException($"Job '{jobId}' did not complete within {timeout ?? TimeSpan.FromSeconds(30)}.");
    }

    public void Dispose()
    {
        BuilderClient.Dispose();
        SearchClient.Dispose();
        BuilderFactory.Dispose();
        SearchFactory.Dispose();

        try { Directory.Delete(StorageRoot, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
