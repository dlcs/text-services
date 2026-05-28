using AsyncKeyedLock;
using TextServices.Pdf;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Pdf;

public interface IPdfGenerationService
{
    Task EnsureGenerated(string id, CancellationToken ct);
    bool IsGenerating(string id);
}

public class PdfGenerationService(
    ITextStore textStore,
    PdfBuilder pdfBuilder,
    AsyncKeyedLocker<string> locker,
    ILogger<PdfGenerationService> logger) : IPdfGenerationService
{
    public async Task EnsureGenerated(string id, CancellationToken ct)
    {
        using (await locker.LockAsync(id, ct))
        {
            var existing = await textStore.LoadPdf(id);
            if (existing != null)
            {
                await existing.DisposeAsync();
                return;
            }

            await GenerateAsync(id, ct);
        }
    }

    public bool IsGenerating(string id) => locker.IsInUse(id);

    private async Task GenerateAsync(string id, CancellationToken ct)
    {
        var text = await textStore.LoadText(id);
        if (text == null)
        {
            logger.LogWarning("PDF generation: no Text artefact for {Id}", id);
            return;
        }

        var manifestJson = await textStore.LoadManifest(id);
        if (manifestJson == null)
        {
            logger.LogWarning("PDF generation: no Manifest for {Id}", id);
            return;
        }

        var pageSequenceJson = await textStore.LoadPageSequence(id);

        logger.LogInformation("Generating PDF for {Id}", id);

        using var ms = new MemoryStream();
        await pdfBuilder.BuildAsync(text, manifestJson, pageSequenceJson, ms, ct);
        ms.Position = 0;
        await textStore.SavePdf(id, ms);

        logger.LogInformation("PDF generated and stored for {Id} ({Bytes} bytes)", id, ms.Length);
    }
}
