using System.Text.Json;
using Hangfire;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;
using TextServices.Core.Providers;
using TextServices.Storage;

namespace TextServices.Builder.Api.Jobs;

/// <summary>
/// Hangfire background job that executes the full text-build pipeline for a single job:
/// <list type="number">
///   <item>Fetch and reduce the IIIF Manifest (when <c>sourceUri</c> was supplied).</item>
///   <item>For each canvas with an ALTO link: fetch the XML and build text artefacts.</item>
///   <item>Persist <c>Text</c> and <c>AutoComplete</c> via <see cref="ITextStore"/>.</item>
/// </list>
/// Per-page ALTO failures are accumulated as warnings and do not abort the job.
/// Only manifest-fetch or storage failures set the job status to <c>Failed</c>.
/// </summary>
public class TextBuildJob(
    BuilderDbContext db,
    IManifestFetcher manifestFetcher,
    IAltoFetcher altoFetcher,
    ITextStore textStore,
    ILogger<TextBuildJob> logger)
{
    // Number of pages between DB progress saves.
    private const int ProgressBatchSize = 10;

    [JobDisplayName("TextBuild: {0}")]
    public async Task ExecuteAsync(string jobId, IJobCancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync(jobId);
        if (job == null)
        {
            logger.LogWarning("TextBuildJob invoked for unknown job {JobId}", jobId);
            return;
        }

        job.Status  = JobStatus.Running;
        job.Started = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            var pages = await GetPages(job, cancellationToken.ShutdownToken);

            job.TotalPages = pages.Count;
            await db.SaveChangesAsync();

            var (wordCount, imageCount, errors) =
                await ProcessPages(job, pages, cancellationToken);

            job.TotalWordCount  = wordCount;
            job.TotalImageCount = imageCount;
            job.Errors          = errors.Count > 0 ? string.Join('\n', errors) : null;
            job.Status          = JobStatus.Completed;
            job.Finished        = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "TextBuildJob completed for {JobId}: {WordCount} words, {ImageCount} images, " +
                "{ErrorCount} page error(s)",
                jobId, wordCount, imageCount, errors.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TextBuildJob failed for {JobId}", jobId);
            job.Status   = JobStatus.Failed;
            job.Errors   = ex.Message;
            job.Finished = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<PageInstruction>> GetPages(
        BuilderJob job, CancellationToken ct)
    {
        if (job.SourceUri != null)
        {
            logger.LogInformation(
                "Fetching manifest for job {JobId} from {Uri}", job.Id, job.SourceUri);

            var result = await manifestFetcher.FetchAndReduce(job.SourceUri, ct);
            await textStore.SaveManifest(job.Id, result.Json);
            job.SourceDataJson = JsonSerializer.Serialize(result.Pages);

            logger.LogInformation(
                "Manifest fetched for {JobId}: {PageCount} canvases", job.Id, result.Pages.Count);

            return result.Pages;
        }

        return job.SourceDataJson != null
            ? JsonSerializer.Deserialize<List<PageInstruction>>(job.SourceDataJson) ?? []
            : [];
    }

    private async Task<(int WordCount, int ImageCount, List<string> Errors)> ProcessPages(
        BuilderJob job,
        IReadOnlyList<PageInstruction> pages,
        IJobCancellationToken cancellationToken)
    {
        var textBuilder = new TextBuilder();
        var errors      = new List<string>();
        int completed   = 0;

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (page.Text != null)
            {
                await FetchAndAddPage(textBuilder, page, errors, cancellationToken.ShutdownToken);
            }

            completed++;
            job.PagesCompleted = completed;

            if (completed % ProgressBatchSize == 0 || completed == pages.Count)
                await db.SaveChangesAsync();
        }

        var result = textBuilder.Build();

        if (!result.IsEmpty)
        {
            await textStore.SaveText(job.Id, result.Text);
            await textStore.SaveAutoComplete(job.Id, result.AutoComplete);
        }

        return (result.Text.Words.Count, result.Text.Images.Length, errors);
    }

    private async Task FetchAndAddPage(
        TextBuilder textBuilder,
        PageInstruction page,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            var xml = await altoFetcher.FetchAsync(page.Text!, ct);

            if (xml == null)
            {
                // 404 or empty — treat as sparse page, not an error.
                logger.LogDebug(
                    "No ALTO content at {AltoUri} for canvas {CanvasId} — skipping",
                    page.Text, page.Id);
                return;
            }

            // We know Text came from an ALTO seeAlso link, so profile="alto" is correct.
            textBuilder.AddPage(page.Id, page.Width, page.Height, xml, profile: "alto");
        }
        catch (Exception ex)
        {
            // Per-page failures are warnings, not job failures.
            logger.LogWarning(ex,
                "Failed to fetch or parse ALTO for canvas {CanvasId} ({AltoUri})",
                page.Id, page.Text);
            errors.Add($"{page.Id}: {ex.Message}");
        }
    }
}
