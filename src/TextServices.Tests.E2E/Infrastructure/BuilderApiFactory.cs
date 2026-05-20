using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the Builder API with
/// all external dependencies replaced for in-process testing:
/// <list type="bullet">
///   <item>EF Core PostgreSQL → EF Core InMemory</item>
///   <item>Hangfire PostgreSQL → Hangfire InMemory</item>
///   <item><see cref="ITextStore"/> → <see cref="FileSystemTextStore"/> in a temp directory</item>
///   <item><see cref="IAltoFetcher"/> → <see cref="FixtureAltoFetcher"/> (local XML files)</item>
///   <item><see cref="IManifestFetcher"/> → <see cref="FixtureManifestFetcher"/> (local JSON files)</item>
/// </list>
/// </summary>
/// <remarks>
/// Uses <see cref="BuilderDbContext"/> as TEntryPoint to avoid ambiguity with the
/// global-namespace <c>Program</c> class that also exists in the Search API project.
/// </remarks>
public class BuilderApiFactory : WebApplicationFactory<BuilderDbContext>
{
    public string StorageRoot { get; }
    public string FixturesRoot { get; }

    public BuilderApiFactory(string storageRoot, string fixturesRoot)
    {
        StorageRoot = storageRoot;
        FixturesRoot = fixturesRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide a fake connection string so Program.cs doesn't throw on startup —
        // the real DbContext and Hangfire registrations are replaced below.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BuilderDb"] = "Host=test-placeholder;",
                ["TextServices:Storage:RootPath"] = StorageRoot,
                ["RunMigrations"] = "false"
            }));

        builder.ConfigureTestServices(services =>
        {
            // ---- EF Core: replace PostgreSQL with InMemory -----------------------
            // Must remove ALL generic descriptors parameterised by BuilderDbContext,
            // including the IDbContextOptionsConfiguration<BuilderDbContext> entries
            // that EF Core uses to build DbContextOptions.  Removing only
            // DbContextOptions<T> leaves the Npgsql configuration action in place,
            // so both providers end up registered — which EF Core rejects.
            var efDescriptors = services
                .Where(d => d.ServiceType.IsGenericType &&
                            d.ServiceType.GetGenericArguments().Contains(typeof(BuilderDbContext)))
                .ToList();
            efDescriptors.ForEach(d => services.Remove(d));
            services.AddDbContext<BuilderDbContext>(o =>
                o.UseInMemoryDatabase("BuilderE2E"));

            // ---- Hangfire: replace PostgreSQL storage with InMemory --------------
            services.AddHangfire(config => config.UseInMemoryStorage());

            // ---- Storage: replace with temp-dir FileSystemTextStore -------------
            services.RemoveAll<ITextStore>();
            services.AddSingleton<ITextStore>(_ =>
                new FileSystemTextStore(new FileSystemTextStoreOptions
                { RootPath = StorageRoot }));

            // ---- Fetchers: replace with fixture-based stubs ---------------------
            services.RemoveAll<IAltoFetcher>();
            services.AddScoped<IAltoFetcher>(_ =>
                new FixtureAltoFetcher(FixturesRoot));

            services.RemoveAll<IManifestFetcher>();
            services.AddScoped<IManifestFetcher>(sp =>
                new FixtureManifestFetcher(
                    FixturesRoot,
                    sp.GetRequiredService<IManifestReducer>()));
        });
    }
}
