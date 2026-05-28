using TextServices.Pdf;
using TextServices.Search.Api.Features.Pdf;

namespace TextServices.Search.Api.Configuration;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPdfServices(this IServiceCollection services)
    {
        services.AddSingleton<PdfBuilder>();
        services.AddSingleton<IPdfGenerationService, PdfGenerationService>();
        services.AddSingleton<IPdfGenerationQueue, PdfGenerationQueue>();
        services.AddHostedService<PdfGenerationBackgroundService>();
        services.AddHttpClient(PdfBuilder.HttpClientName)
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(60);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0 (+https://github.com/dlcs/text-services)");
            });

        return services;
    }
}
