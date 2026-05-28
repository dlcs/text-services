using Microsoft.Extensions.Options;
using TextServices.Search.Api.Configuration;

namespace TextServices.Search.Api.Features.Pdf;

public class PdfGenerationBackgroundService(
    IPdfGenerationQueue queue,
    IPdfGenerationService generationService,
    IOptions<SearchApiOptions> options,
    ILogger<PdfGenerationBackgroundService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(options.Value.PdfTriggerMaxConcurrency);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);
            _ = ProcessAsync(id);
        }
    }

    private async Task ProcessAsync(string id)
    {
        try
        {
            await generationService.EnsureGenerated(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background PDF generation failed for {Id}", id);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
