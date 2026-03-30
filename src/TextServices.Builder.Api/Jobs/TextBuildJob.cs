using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Hangfire;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Storage;

namespace TextServices.Builder.Api.Jobs;

/// <summary>
/// Hangfire background job that executes the full text-build pipeline for a single job:
/// <list type="number">
///   <item>Fetch and reduce the IIIF Manifest (when <c>sourceUri</c> was supplied).</item>
///   <item>Fetch all ALTO files concurrently (bounded by <see cref="TextServicesOptions.MaxConcurrentAltoFetches"/>).</item>
///   <item>Feed pages to <see cref="TextBuilder"/> in original canvas order.</item>
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
    TextServicesOptions options,
    ILogger<TextBuildJob> logger)
{
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
        // Fetch all ALTO files concurrently, bounded by the semaphore.
        // Results are returned as an ordered array matching the pages list,
        // so TextBuilder receives canvases in the correct sequence.
        //
        // TODO: The right concurrency limit depends on where the ALTO files live.
        //   - Third-party HTTP (e.g. Wellcome, Internet Archive): keep low (4–8) for
        //     politeness and to avoid rate-limiting.
        //   - Internal/trusted HTTP: can be higher (16–32).
        //   - S3 (s3:// or https://*.s3.amazonaws.com): S3 supports very high
        //     parallelism on the same bucket; 64–128 is reasonable. When an S3
        //     ITextStore is in use the ALTO URIs will typically be pre-signed HTTPS
        //     URLs or s3:// keys fetched via the AWS SDK — detect by scheme or
        //     hostname pattern and use a higher limit for those.
        //   Consider deriving the limit from the scheme/host of pages[0].Text, or
        //   adding a per-host override table to TextServicesOptions.
        var semaphore = new SemaphoreSlim(options.MaxConcurrentAltoFetches);

        var fetchTasks = pages
            .Select(page => FetchWithSemaphoreAsync(page, semaphore, cancellationToken.ShutdownToken))
            .ToList();

        var fetched = await Task.WhenAll(fetchTasks);

        // Build text in original canvas order (TextBuilder requires sequential input).
        var textBuilder = new TextBuilder();
        var errors      = fetched.Where(r => r.Error != null).Select(r => r.Error!).ToList();
        int completed   = 0;

        foreach (var (page, xml, _) in fetched)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (xml != null)
                textBuilder.AddPage(page.Id, page.Width, page.Height, xml, profile: "alto");

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

            var figuresJson = BuildFiguresJson(result.Text);
            if (figuresJson != null)
                await textStore.SaveFigures(job.Id, figuresJson);
        }

        return (result.Text.Words.Count, result.Text.Images.Length, errors);
    }

    /// <summary>
    /// Builds a IIIF Presentation 3 AnnotationPage JSON string from the ComposedBlocks
    /// in the given <paramref name="text"/>.  Returns <see langword="null"/> when there
    /// are no blocks with non-zero dimensions (nothing to surface).
    /// </summary>
    /// <remarks>
    /// The top-level <c>id</c> is left empty — it is replaced with the request URL at
    /// serve time by the Search API, the same way the stored Manifest's <c>id</c> is
    /// patched by the text-augmented endpoint.
    /// </remarks>
    private static string? BuildFiguresJson(Text text)
    {
        if (text.ComposedBlocks == null || text.ComposedBlocks.Length == 0)
            return null;

        var items = new JsonArray();

        foreach (var cb in text.ComposedBlocks)
        {
            // Skip degenerate blocks (zero area) that occasionally appear in ALTO.
            if (cb.W <= 0 || cb.H <= 0) continue;
            if (cb.ImageIndex < 0 || cb.ImageIndex >= text.Images.Length) continue;

            var canvasId  = text.Images[cb.ImageIndex].ImageIdentifier;
            var blockType = string.IsNullOrWhiteSpace(cb.BlockType) ? "Unknown" : cb.BlockType;

            items.Add(new JsonObject
            {
                ["id"]         = $"f{cb.ComposedBlockIndex}",
                ["type"]       = "Annotation",
                ["motivation"] = "tagging",
                ["body"] = new JsonObject
                {
                    ["type"]   = "TextualBody",
                    ["value"]  = blockType,
                    ["format"] = "text/plain",
                },
                ["target"] = $"{canvasId}#xywh={cb.X},{cb.Y},{cb.W},{cb.H}",
            });
        }

        if (items.Count == 0) return null;

        var page = new JsonObject
        {
            ["@context"] = "http://iiif.io/api/presentation/3/context.json",
            ["id"]       = "",   // patched at serve time
            ["type"]     = "AnnotationPage",
            ["label"]    = new JsonObject
            {
                ["en"] = new JsonArray("Figures, tables and illustrations"),
            },
            ["items"] = items,
        };

        return page.ToJsonString();
    }

    private async Task<(PageInstruction Page, XElement? Xml, string? Error)> FetchWithSemaphoreAsync(
        PageInstruction page,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        if (page.Text == null)
            return (page, null, null);

        await semaphore.WaitAsync(ct);
        try
        {
            var xml = await altoFetcher.FetchAsync(page.Text, ct);

            if (xml == null)
                logger.LogDebug(
                    "No ALTO content at {AltoUri} for canvas {CanvasId} — skipping",
                    page.Text, page.Id);

            return (page, xml, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch or parse ALTO for canvas {CanvasId} ({AltoUri})",
                page.Id, page.Text);
            return (page, null, $"{page.Id}: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }
}
