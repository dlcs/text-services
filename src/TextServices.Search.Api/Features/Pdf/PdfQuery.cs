using AsyncKeyedLock;
using MediatR;
using TextServices.Pdf;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Pdf;

/// <summary>
/// Synchronous request: returns a readable stream over the PDF for the given job ID,
/// generating and storing it on first access.  Returns <see langword="null"/> when no
/// text artefact exists for the ID (404 territory).
/// </summary>
public record PdfRequest(string Id) : IRequest<Stream?>;

/// <summary>
/// Async-trigger request: starts PDF generation in the background without waiting for
/// completion. Returns <see langword="true"/> if generation was started or is already
/// in progress; <see langword="false"/> if the PDF already exists (no work needed).
/// </summary>
public record PdfTriggerRequest(string Id) : IRequest<bool>;

public class PdfHandler(
    ITextStore               textStore,
    ITextCache               cache,
    PdfBuilder               pdfBuilder,
    AsyncKeyedLocker<string> locker,
    ILogger<PdfHandler>      logger)
    : IRequestHandler<PdfRequest, Stream?>,
      IRequestHandler<PdfTriggerRequest, bool>
{
    // -------------------------------------------------------------------------
    // Synchronous GET — generates on demand, blocks until complete
    // -------------------------------------------------------------------------

    public async Task<Stream?> Handle(PdfRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Pdf, ct)) return null;

        // Fast path — PDF already exists
        var existing = await textStore.LoadPdf(request.Id);
        if (existing != null) return existing;

        // Verify text artefact exists before trying to build
        if (!await textStore.Exists(request.Id)) return null;

        // Acquire per-key lock, double-check, then generate
        using (await locker.LockAsync(request.Id, ct))
        {
            var afterLock = await textStore.LoadPdf(request.Id);
            if (afterLock != null) return afterLock;

            await GeneratePdfAsync(request.Id, CancellationToken.None);
        }

        return await textStore.LoadPdf(request.Id);
    }

    // -------------------------------------------------------------------------
    // Async POST trigger — fires and forgets, returns immediately
    // -------------------------------------------------------------------------

    public async Task<bool> Handle(PdfTriggerRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Pdf, ct)) return false;

        // Already done
        var existing = await textStore.LoadPdf(request.Id);
        if (existing != null)
        {
            await existing.DisposeAsync();
            return false;
        }

        if (!await textStore.Exists(request.Id)) return false;

        // If the lock is already held (generation in progress), return true immediately
        // without queuing another generation.
        if (locker.IsInUse(request.Id)) return true;

        // Fire background generation — do not await
        _ = Task.Run(async () =>
        {
            try
            {
                using (await locker.LockAsync(request.Id))
                {
                    // Double-check inside lock
                    var check = await textStore.LoadPdf(request.Id);
                    if (check != null) { check.Dispose(); return; }

                    await GeneratePdfAsync(request.Id, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background PDF generation failed for {Id}", request.Id);
            }
        });

        return true;
    }

    // -------------------------------------------------------------------------
    // Shared generation logic
    // -------------------------------------------------------------------------

    private async Task GeneratePdfAsync(string id, CancellationToken ct)
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

        logger.LogInformation("Generating PDF for {Id}", id);

        using var ms = new MemoryStream();
        await pdfBuilder.BuildAsync(text, manifestJson, ms, ct);
        ms.Position = 0;
        await textStore.SavePdf(id, ms);

        logger.LogInformation("PDF generated and stored for {Id} ({Bytes} bytes)", id, ms.Length);
    }
}
