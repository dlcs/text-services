using MediatR;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Search.Api.Features.Pdf;

/// <summary>
/// Synchronous request: returns a readable stream over the PDF for the given job ID,
/// generating and storing it on first access. Returns <see langword="null"/> when no
/// text artefact exists for the ID (404 territory).
/// </summary>
public record PdfRequest(string Id) : IRequest<Stream?>;

/// <summary>
/// Async-trigger request: enqueues background PDF generation without waiting for completion.
/// </summary>
public record PdfTriggerRequest(string Id) : IRequest<PdfTriggerResult>;

public enum PdfTriggerResult
{
    /// <summary>PDF already exists; no work needed.</summary>
    AlreadyExists,
    /// <summary>Generation is queued or already in progress.</summary>
    Queued,
    /// <summary>Trigger queue is full; caller should retry later.</summary>
    ServiceBusy,
    /// <summary>No text artefact exists for this ID, or the service is disabled.</summary>
    NotFound,
}

public class PdfHandler(
    ITextStore textStore,
    ITextCache cache,
    IPdfGenerationService generationService,
    IPdfGenerationQueue triggerQueue)
    : IRequestHandler<PdfRequest, Stream?>,
      IRequestHandler<PdfTriggerRequest, PdfTriggerResult>
{
    // -------------------------------------------------------------------------
    // Synchronous GET — generates on demand, blocks until complete
    // -------------------------------------------------------------------------

    public async Task<Stream?> Handle(PdfRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Pdf, ct)) return null;

        var existing = await textStore.LoadPdf(request.Id);
        if (existing != null) return existing;

        if (!await textStore.Exists(request.Id)) return null;

        await generationService.EnsureGenerated(request.Id, ct);

        return await textStore.LoadPdf(request.Id);
    }

    // -------------------------------------------------------------------------
    // Async POST trigger — enqueues generation, returns immediately
    // -------------------------------------------------------------------------

    public async Task<PdfTriggerResult> Handle(PdfTriggerRequest request, CancellationToken ct)
    {
        if (!await cache.IsEnabledAsync(request.Id, JobServices.Pdf, ct)) return PdfTriggerResult.NotFound;

        var existing = await textStore.LoadPdf(request.Id);
        if (existing != null)
        {
            await existing.DisposeAsync();
            return PdfTriggerResult.AlreadyExists;
        }

        if (!await textStore.Exists(request.Id)) return PdfTriggerResult.NotFound;

        if (generationService.IsGenerating(request.Id)) return PdfTriggerResult.Queued;

        return triggerQueue.TryEnqueue(request.Id) ? PdfTriggerResult.Queued : PdfTriggerResult.ServiceBusy;
    }
}
