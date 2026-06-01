using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the Builder API with
/// all external dependencies replaced for in-process testing:
/// <list type="bullet">
///   <item>EF Core PostgreSQL → EF Core PostgreSQL (TestContainers, via config override)</item>
///   <item>Hangfire PostgreSQL → Hangfire InMemory</item>
///   <item><see cref="ITextStore"/> → temp directory (via config override)</item>
///   <item><see cref="IAltoFetcher"/> → <see cref="FixtureAltoFetcher"/> (local XML files)</item>
///   <item><see cref="IManifestFetcher"/> → <see cref="FixtureManifestFetcher"/> (local JSON files)</item>
/// </list>
/// </summary>
/// <remarks>
/// Uses <see cref="BuilderDbContext"/> as TEntryPoint to avoid ambiguity with the
/// global-namespace <c>Program</c> class that also exists in the Search API project.
/// </remarks>
public class BuilderApiFactory(string connectionString, string storageRoot, string fixturesRoot)
    : WebApplicationFactory<BuilderDbContext>
{
    public string StorageRoot { get; } = storageRoot;
    public string FixturesRoot { get; } = fixturesRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BuilderDb"] = connectionString,
                ["TextServices:Storage:RootPath"] = StorageRoot,
                ["RunMigrations"] = "false"
            }));

        builder.ConfigureTestServices(services =>
        {
            // ---- Hangfire: replace PostgreSQL storage with InMemory --------------
            services.AddHangfire(config => config.UseInMemoryStorage());

            // ---- Storage: use shared temp directory -----------------------------
            services.AddSingleton<ITextStore>(_ =>
                new FileSystemTextStore(
                    new FileSystemTextStoreOptions { RootPath = StorageRoot },
                    NullLogger<FileSystemTextStore>.Instance));

            // ---- Fetchers: replace with fixture-based stubs ---------------------
            services.AddScoped<IAltoFetcher>(_ =>
                new FixtureAltoFetcher(FixturesRoot));

            services.AddScoped<IManifestFetcher>(sp =>
                new FixtureManifestFetcher(
                    FixturesRoot,
                    sp.GetRequiredService<IManifestReducer>()));
        });
    }
}
