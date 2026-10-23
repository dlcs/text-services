using Hangfire;
using MediatR;
using Microsoft.Extensions.Options;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Jobs;
using TextServices.Storage;

namespace TextServices.Builder.Api.Features.Jobs;

/// <param name="Id">Job to reprocess.</param>
/// <param name="Instruction">
/// Optional replacement job definition. When null, the stored definition is reused.
/// Must already be validated, with an <c>Id</c> matching <paramref name="Id"/>.
/// </param>
public record ReprocessJobRequest(string Id, JobInstruction? Instruction = null) : IRequest<ReprocessJobResult>;

public enum ReprocessStatus { Ok, NotFound, Conflict }

public record ReprocessJobResult(ReprocessStatus Status, JobResponse? Response);

/// <summary>
/// Resets an existing job back to Waiting and re-enqueues it via Hangfire so the
/// manifest and all ALTO files are re-fetched and the Text/AutoComplete artefacts
/// are rebuilt from scratch.
///
/// When an instruction is supplied it replaces the whole stored job definition (PUT
/// semantics): every caller-settable field is overwritten, and omitted fields are cleared
/// or reset to their defaults rather than kept.
///
/// Returns 409 if the job is currently Running — wait for it to finish first.
/// </summary>
public class ReprocessJobHandler(
    BuilderDbContext db,
    IBackgroundJobClient hangfire,
    ITextStore textStore,
    IHttpClientFactory httpClientFactory,
    IOptions<TextServicesOptions> options,
    ILogger<ReprocessJobHandler> logger)
    : IRequestHandler<ReprocessJobRequest, ReprocessJobResult>
{
    public async Task<ReprocessJobResult> Handle(ReprocessJobRequest request, CancellationToken ct)
    {
        var job = await db.Jobs.FindAsync([request.Id], ct);
        if (job == null)
            return new ReprocessJobResult(ReprocessStatus.NotFound, null);

        // Can't safely re-enqueue while the worker is actively processing.
        if (job.Status == JobStatus.Running)
            return new ReprocessJobResult(ReprocessStatus.Conflict, JobResponse.From(job, options.Value));

        // Remove the old Hangfire job entry (may be queued, awaiting retry, or
        // already finished — Delete is a no-op if the job no longer exists).
        if (job.HangfireJobId != null)
            hangfire.Delete(job.HangfireJobId);

        // Delete all stored artefacts so stale derivatives don't survive the rebuild.
        await textStore.DeleteArtefacts(job.Id);

        // Notify the Search API to evict its in-process cache for this key.
        _ = InvalidateCacheAsync(job.Id);

        // Replace the stored definition. For sourceUri jobs this also discards the reduced
        // page list from the previous run, so only pdf entries in the new body are kept.
        request.Instruction?.ApplyTo(job);

        // Reset all transient fields.
        job.Status = JobStatus.Waiting;
        job.Started = null;
        job.Finished = null;
        job.TotalPages = 0;
        job.PagesCompleted = 0;
        job.TotalWordCount = 0;
        job.TotalImageCount = 0;
        job.Errors = null;
        job.HangfireJobId = null;
        job.InvocationCount++;

        await db.SaveChangesAsync(ct);

        var hangfireJobId = hangfire.Enqueue<TextBuildJob>(j => j.ExecuteAsync(job.Id, null!));
        job.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);

        return new ReprocessJobResult(ReprocessStatus.Ok, JobResponse.From(job, options.Value));
    }

    private async Task InvalidateCacheAsync(string id)
    {
        if (string.IsNullOrEmpty(options.Value.SearchApiBaseUrl)) return;
        try
        {
            var http = httpClientFactory.CreateClient();
            var url = $"{options.Value.SearchApiBaseUrl.TrimEnd('/')}/cache/v1/{id}";
            await http.DeleteAsync(url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invalidate Search API cache for key {Id}", id);
        }
    }
}
