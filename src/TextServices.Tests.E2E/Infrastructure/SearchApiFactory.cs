using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.E2E.Infrastructure;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the Search API pointing at
/// the same temp storage directory as <see cref="BuilderApiFactory"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="TextCache"/> as TEntryPoint to avoid ambiguity with the
/// global-namespace <c>Program</c> class that also exists in the Builder API project.
/// </remarks>
public class SearchApiFactory(string storageRoot) : WebApplicationFactory<TextCache>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TextServices:Storage:FileSystem:RootPath"] = storageRoot,
                ["TextServices:BaseUrl"] = "http://localhost"
            }));

        builder.ConfigureTestServices(services =>
            services.AddSingleton<ITextStore>(_ =>
                new FileSystemTextStore(
                    new FileSystemTextStoreOptions { RootPath = storageRoot },
                    NullLogger<FileSystemTextStore>.Instance)));
    }
}
