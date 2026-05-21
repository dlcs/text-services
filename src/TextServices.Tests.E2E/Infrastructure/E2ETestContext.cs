using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TextServices.Builder.Api.Data;

namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// Collection fixture shared across all E2E tests (see <see cref="E2ECollectionDefinition"/>).
/// Starts a PostgreSQL TestContainer once for the entire test run, applies EF migrations,
/// then tears everything down afterwards.
/// </summary>
public sealed class E2ETestContext : IAsyncLifetime
{
    private static readonly string FixturesRoot = Path.Combine(
        AppContext.BaseDirectory, "Fixtures");

    public string StorageRoot { get; } =
        Path.Combine(Path.GetTempPath(), "TextServicesE2E_" + Guid.NewGuid().ToString("N"));

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public BuilderApiFactory BuilderFactory { get; private set; } = null!;
    public SearchApiFactory SearchFactory { get; private set; } = null!;

    public HttpClient BuilderClient { get; private set; } = null!;
    public HttpClient SearchClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(StorageRoot);

        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<BuilderDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var ctx = new BuilderDbContext(options);
        await ctx.Database.MigrateAsync();

        BuilderFactory = new BuilderApiFactory(_postgres.GetConnectionString(), StorageRoot, FixturesRoot);
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

    public async Task DisposeAsync()
    {
        BuilderClient?.Dispose();
        SearchClient?.Dispose();
        BuilderFactory?.Dispose();
        SearchFactory?.Dispose();

        try { Directory.Delete(StorageRoot, recursive: true); }
        catch { /* best-effort */ }

        await _postgres.DisposeAsync();
    }
}
