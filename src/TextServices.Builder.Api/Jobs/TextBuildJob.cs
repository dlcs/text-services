using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Hangfire;
using Microsoft.Extensions.Options;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;
using TextServices.Builder.Api.Services.Notifications;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Storage;

namespace TextServices.Builder.Api.Jobs;

/// <summary>
/// Hangfire background job that executes the full text-build pipeline for a single job:
/// <list type="number">
///   <item>Fetch and reduce the IIIF Manifest (when <c>sourceUri</c> was supplied).</item>
///   <item>Fetch all ALTO files concurrently (bounded by <see cref="TextServicesOptions.MaxConcurrentPageFetches"/>).</item>
///   <item>Feed pages to <see cref="TextBuilder"/> in original canvas order.</item>
///   <item>Persist <c>Text</c> and <c>AutoComplete</c> via <see cref="ITextStore"/>.</item>
/// </list>
/// Per-page ALTO failures are accumulated as warnings and do not abort the job.
/// Only manifest-fetch or storage failures set the job status to <c>Failed</c>.
/// </summary>
public class TextBuildJob(
    BuilderDbContext db,
    IManifestFetcher manifestFetcher,
    IManifestSynthesiser manifestSynthesiser,
    IAltoFetcher altoFetcher,
    IVttFetcher vttFetcher,
    IAnnotationPageFetcher annotationPageFetcher,
    ITextStore textStore,
    IJobNotifier jobNotifier,
    IOptions<TextServicesOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<TextBuildJob> logger)
{
    private const int ProgressBatchSize = 10;

    // StringContent carries either VTT text or AnnotationPage JSON, routed by page.Format.
    private record FetchedPage(PageInstruction Page, XElement? Xml, string? StringContent, string? Error);

    [JobDisplayName("TextBuild: {0}")]
    public async Task ExecuteAsync(string jobId, IJobCancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync(jobId);
        if (job == null)
        {
            logger.LogWarning("TextBuildJob invoked for unknown job {JobId}", jobId);
            return;
        }

        job.Status = JobStatus.Running;
        job.Started = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            var pages = await GetPages(job, cancellationToken.ShutdownToken);
            job.TotalPages = pages.Count;
            await db.SaveChangesAsync();

            var (wordCount, imageCount, errors, fulfilled) =
                await ProcessPages(job, pages, cancellationToken);

            job.TotalWordCount = wordCount;
            job.TotalImageCount = imageCount;
            job.Errors = errors.Count > 0 ? string.Join('\n', errors) : null;
            job.FulfilledServices = (int)fulfilled;
            job.Status = JobStatus.Completed;
            job.Finished = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "TextBuildJob completed for {JobId}: {WordCount} words, {ImageCount} images, " +
                "{ErrorCount} page error(s)",
                jobId, wordCount, imageCount, errors.Count);

            await jobNotifier.Notify(
                new JobCompletionNotification(job.Id, job.Status, job.Finished,
                    job.TotalPages, job.TotalWordCount, job.Errors),
                cancellationToken.ShutdownToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TextBuildJob failed for {JobId}", jobId);
            job.Status = JobStatus.Failed;
            job.Errors = ex.Message;
            job.FulfilledServices = (int)JobServices.None;
            job.Finished = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            await jobNotifier.Notify(
                new JobCompletionNotification(job.Id, job.Status, job.Finished,
                    job.TotalPages, job.TotalWordCount, job.Errors),
                cancellationToken.ShutdownToken);
        }
    }

    private async Task<IReadOnlyList<PageInstruction>> GetPages(BuilderJob job, CancellationToken ct)
    {
        var services = (JobServices)job.Services;
        var needsManifest = services.HasFlag(JobServices.TextAugmented) || services.HasFlag(JobServices.Pdf);

        if (job.SourceUri != null)
        {
            logger.LogInformation("Fetching manifest for job {JobId} from {Uri}", job.Id, job.SourceUri);

            var result = await manifestFetcher.FetchAndReduce(job.SourceUri, ct);

            if (needsManifest)
                await textStore.SaveManifest(job.Id, result.Json);

            job.SourceDataJson = JsonSerializer.Serialize(result.Pages);

            logger.LogInformation("Manifest fetched for {JobId}: {PageCount} canvases", job.Id, result.Pages.Count);
            return result.Pages;
        }

        // sourceData path: deserialise inline pages, then synthesise a skeleton manifest
        // so text-augmented and PDF work identically regardless of how the job was submitted.
        IReadOnlyList<PageInstruction> pages = job.SourceDataJson != null
            ? JsonSerializer.Deserialize<List<PageInstruction>>(job.SourceDataJson) ?? []
            : [];

        if (needsManifest && pages.Count > 0)
        {
            var syntheticJson = manifestSynthesiser.Synthesise(pages);
            await textStore.SaveManifest(job.Id, syntheticJson);
            logger.LogDebug("Synthetic manifest saved for {JobId} ({PageCount} canvases)", job.Id, pages.Count);
        }

        return pages;
    }

    private async Task<(int WordCount, int ImageCount, List<string> Errors, JobServices Fulfilled)> ProcessPages(
        BuilderJob job,
        IReadOnlyList<PageInstruction> pages,
        IJobCancellationToken cancellationToken)
    {
        // Fetch all pages concurrently, bounded by MaxConcurrentPageFetches.
        // Results are stored into a pre-allocated array so TextBuilder receives
        // canvases in the correct sequence.
        //
        // TODO: The right concurrency limit depends on where the text files live.
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

        // pdf-type pages embed an existing PDF; they have no text to build.
        // Custom-type pages are generated at PDF render time; no text to fetch.
        var pagesToFetch = pages.Where(page => page.Type == null).ToList();
        var fetched = new FetchedPage[pagesToFetch.Count];

        await Parallel.ForEachAsync(Enumerable.Range(0, pagesToFetch.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Value.MaxConcurrentPageFetches,
                CancellationToken = cancellationToken.ShutdownToken
            },
            async (i, ct) => { fetched[i] = await FetchPageAsync(pagesToFetch[i], ct); });

        // Build text in original canvas order (TextBuilder requires sequential input).
        var textBuilder = new TextBuilder(loggerFactory);
        var errors = fetched.Where(r => r.Error != null).Select(r => r.Error!).ToList();
        int completed = 0;

        foreach (var fetchedPage in fetched)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = fetchedPage.Page;
            if (fetchedPage.Xml != null)
            {
                textBuilder.AddPage(page.Id!, page.Width, page.Height, fetchedPage.Xml, page.Profile, page.Label);
            }
            else if (fetchedPage.StringContent != null)
            {
                textBuilder.AddTranscriptPage(page.Id!, page.Width, page.Height, fetchedPage.StringContent,
                    profile: page.Profile, format: page.Format, label: page.Label);
            }

            completed++;
            job.PagesCompleted = completed;

            if (options.Value.ReportBatchProgress && (completed % ProgressBatchSize == 0 || completed == pages.Count))
            {
                await db.SaveChangesAsync();
            }
        }

        var result = textBuilder.Build();
        var services = (JobServices)job.Services;

        string? figuresJson = null;
        string? annotationsJson = null;
        bool textSaved = false;

        if (!result.IsEmpty)
        {
            // Text artefact is needed by the Search endpoint and by PDF generation.
            bool needsText = services.HasFlag(JobServices.Search) ||
                             services.HasFlag(JobServices.Pdf);
            if (needsText)
            {
                await textStore.SaveText(job.Id, result.Text);
                textSaved = true;
            }

            if (services.HasFlag(JobServices.Autocomplete)) await textStore.SaveAutoComplete(job.Id, result.AutoComplete);

            if (services.HasFlag(JobServices.FullText) && !string.IsNullOrEmpty(result.Text.RawFullText))
            {
                await textStore.SaveRawText(job.Id, result.Text.RawFullText);
            }

            if (services.HasFlag(JobServices.Figures))
            {
                figuresJson = BuildFiguresJson(result.Text);
                if (figuresJson != null) await textStore.SaveFigures(job.Id, figuresJson);
            }

            if (services.HasFlag(JobServices.Annotations))
            {
                annotationsJson = BuildManifestAnnotationsJson(result.Text);
                if (annotationsJson != null) await textStore.SaveAnnotations(job.Id, annotationsJson);
            }
        }

        // Determine which services were actually fulfilled so consumers know what's available.
        var needsManifest = services.HasFlag(JobServices.TextAugmented) || services.HasFlag(JobServices.Pdf);
        var manifestSaved = needsManifest && (job.SourceUri != null || pages.Count > 0);

        var fulfilled = JobServices.None;
        if (textSaved)
        {
            if (services.HasFlag(JobServices.Search)) fulfilled |= JobServices.Search;
            if (services.HasFlag(JobServices.Pdf)) fulfilled |= JobServices.Pdf;
        }
        if (!result.IsEmpty && services.HasFlag(JobServices.Autocomplete))
            fulfilled |= JobServices.Autocomplete;
        if (!result.IsEmpty && services.HasFlag(JobServices.FullText) && !string.IsNullOrEmpty(result.Text.RawFullText))
            fulfilled |= JobServices.FullText;
        if (!result.IsEmpty && services.HasFlag(JobServices.Figures) && figuresJson != null)
            fulfilled |= JobServices.Figures;
        if (!result.IsEmpty && services.HasFlag(JobServices.Annotations) && annotationsJson != null)
            fulfilled |= JobServices.Annotations;
        if (services.HasFlag(JobServices.TextAugmented) && manifestSaved)
            fulfilled |= JobServices.TextAugmented;

        // Always write capabilities using the fulfilled bitmask so the Search API only exposes
        // endpoints that actually have artefacts (e.g. no search service when text is empty).
        await textStore.SaveCapabilities(job.Id, (int)fulfilled);

        // For sourceData jobs, persist the full page sequence (including pdf-embed entries
        // that have no canvas in the synthesised manifest) so the PDF builder can
        // reconstruct the complete assembly order, including embedded PDFs.
        if (job.SourceUri == null && services.HasFlag(JobServices.Pdf))
        {
            var pageSequenceJson = BuildPageSequenceJson(job, pages);
            await textStore.SavePageSequence(job.Id, pageSequenceJson);
        }

        return (result.Text.Words.Count, result.Text.Images.Length, errors, fulfilled);
    }

    private static string BuildPageSequenceJson(BuilderJob job, IReadOnlyList<PageInstruction> pages)
    {
        Dictionary<string, CustomPageType>? customTypes = null;
        if (job.CustomTypesJson != null)
        {
            customTypes = JsonSerializer.Deserialize<Dictionary<string, CustomPageType>>(job.CustomTypesJson);
        }

        var pageArray = new JsonArray();
        foreach (var page in pages)
        {
            JsonObject entry;
            if (string.Equals(page.Type, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                entry = new JsonObject { ["type"] = "pdf", ["input"] = page.Input };
            }
            else if (page.Type != null)
            {
                CustomPageType? cpt = null;
                customTypes?.TryGetValue(page.Type, out cpt);
                entry = new JsonObject { ["type"] = page.Type, ["canvasId"] = page.Id };
                if (page.Width > 0) entry["width"] = page.Width;
                if (page.Height > 0) entry["height"] = page.Height;
                if (cpt?.Message != null) entry["message"] = cpt.Message;
            }
            else
            {
                entry = new JsonObject { ["canvasId"] = page.Id };
            }
            pageArray.Add(entry);
        }

        var root = new JsonObject { ["pages"] = pageArray };
        if (job.Title != null) root["title"] = job.Title;
        return root.ToJsonString();
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
        if (text.ComposedBlocks == null || text.ComposedBlocks.Length == 0) return null;

        var items = new JsonArray();

        foreach (var cb in text.ComposedBlocks)
        {
            // Skip degenerate blocks (zero area) that occasionally appear in ALTO.
            if (cb.W <= 0 || cb.H <= 0) continue;
            if (cb.ImageIndex < 0 || cb.ImageIndex >= text.Images.Length) continue;

            var canvasId = text.Images[cb.ImageIndex].ImageIdentifier;
            var blockType = string.IsNullOrWhiteSpace(cb.BlockType) ? "Unknown" : cb.BlockType;

            items.Add(new JsonObject
            {
                ["id"] = $"f{cb.ComposedBlockIndex}",
                ["type"] = "Annotation",
                ["motivation"] = "tagging",
                ["body"] = new JsonObject
                {
                    ["type"] = "TextualBody",
                    ["value"] = blockType,
                    ["format"] = "text/plain",
                },
                ["target"] = $"{canvasId}#xywh={cb.X},{cb.Y},{cb.W},{cb.H}",
            });
        }

        if (items.Count == 0) return null;

        var page = new JsonObject
        {
            ["@context"] = "http://iiif.io/api/presentation/3/context.json",
            ["id"] = "",   // patched at serve time
            ["type"] = "AnnotationPage",
            ["label"] = new JsonObject
            {
                ["en"] = new JsonArray("Figures, tables and illustrations"),
            },
            ["items"] = items,
        };

        return page.ToJsonString();
    }

    /// <summary>
    /// Builds a manifest-level IIIF <c>AnnotationPage</c> JSON string containing
    /// one line-level annotation per text line across all canvases.
    /// Returns <see langword="null"/> when the text has no words.
    /// </summary>
    /// <remarks>
    /// Annotation <c>id</c> values are stored as relative paths (e.g. <c>"anno/0"</c>)
    /// and are prefixed with the request URL at serve time by the Search API, following
    /// the same pattern as <see cref="BuildFiguresJson"/>.
    /// </remarks>
    private static string? BuildManifestAnnotationsJson(Text text)
    {
        if (text.Images.Length == 0 || text.Words.Count == 0) return null;

        var items = new JsonArray();
        var annoIndex = 0;

        for (var i = 0; i < text.Images.Length; i++)
        {
            var image = text.Images[i];
            var canvasId = image.ImageIdentifier;
            var isTemporal = image.IsTemporalContent;

            var canvasWords = text.Words.Values
                .Where(w => w.Idx == i)
                .OrderBy(w => w.Wd)
                .ToList();

            foreach (var lineGroup in canvasWords.GroupBy(w => w.Li).OrderBy(g => g.Key))
            {
                var lineWords = lineGroup.ToList();
                var lineText = string.Join(" ", lineWords.Select(w => w.ContentRaw));

                string target;
                if (isTemporal)
                {
                    var startMs = lineWords.Min(w => w.StartMs);
                    var endMs = lineWords.Max(w => w.EndMs);
                    target = $"{canvasId}#t={Sec(startMs)},{Sec(endMs)}";
                }
                else
                {
                    var x = lineWords.Min(w => w.X);
                    var y = lineWords.Min(w => w.Y);
                    var right = lineWords.Max(w => w.X + w.W);
                    var bottom = lineWords.Max(w => w.Y + w.H);
                    target = $"{canvasId}#xywh={x},{y},{right - x},{bottom - y}";
                }

                items.Add(new JsonObject
                {
                    ["id"] = $"anno/{annoIndex++}",
                    ["type"] = "Annotation",
                    ["motivation"] = "supplementing",
                    ["body"] = new JsonObject
                    {
                        ["type"] = "TextualBody",
                        ["value"] = lineText,
                        ["format"] = "text/plain",
                    },
                    ["target"] = target,
                });
            }
        }

        if (items.Count == 0) return null;

        var page = new JsonObject
        {
            ["@context"] = new JsonArray(
                "http://iiif.io/api/presentation/3/context.json",
                "https://iiif.io/api/extension/text-granularity/context.json"),
            ["id"] = "",   // patched at serve time
            ["type"] = "AnnotationPage",
            ["profile"] = "https://dlcs.io/profiles/all-text",
            ["textGranularity"] = "line",
            ["label"] = new JsonObject { ["en"] = new JsonArray("Text of all canvases") },
            ["items"] = items,
        };

        return page.ToJsonString();
    }

    private static string Sec(int ms) =>
        (ms / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsAnnotationPage(PageInstruction page) =>
        page.Format == Core.Providers.W3cAnnotationTextFormatProvider.FormatSentinel;

    private static bool IsVttPage(PageInstruction page) =>
        ContainsIgnoreCase(page.Profile, "text/vtt") || ContainsIgnoreCase(page.Format, "text/vtt") ||
        ContainsIgnoreCase(page.Profile, "vtt") || ContainsIgnoreCase(page.Format, "vtt") ||
        ContainsIgnoreCase(page.Label, "vtt") ||
        ContainsIgnoreCase(page.Label, "webvtt") ||
        ContainsIgnoreCase(page.Label, "transcript");

    private static bool ContainsIgnoreCase(string? value, string term) =>
        value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private async Task<FetchedPage> FetchPageAsync(PageInstruction page, CancellationToken ct)
    {
        if (page.TextUri == null) return new FetchedPage(page, null, null, null);

        try
        {
            if (IsAnnotationPage(page))
            {
                var json = await annotationPageFetcher.FetchAsync(page.TextUri, ct);
                if (json == null)
                    logger.LogDebug("Annotation page not found for canvas {CanvasId}: {Url}", page.Id, page.TextUri);
                else
                    logger.LogDebug("Annotation page fetched for canvas {CanvasId}: {Url}", page.Id, page.TextUri);
                return new FetchedPage(page, null, json, null);
            }

            if (IsVttPage(page))
            {
                var vtt = await vttFetcher.FetchAsync(page.TextUri, ct);
                if (vtt == null)
                    logger.LogDebug("VTT not found for canvas {CanvasId}: {Url}", page.Id, page.TextUri);
                else
                    logger.LogDebug("VTT fetched for canvas {CanvasId}: {Url}", page.Id, page.TextUri);
                return new FetchedPage(page, null, vtt, null);
            }

            var xml = await altoFetcher.FetchAsync(page.TextUri, ct);
            if (xml == null)
                logger.LogDebug("No ALTO content at {AltoUri} for canvas {CanvasId} — skipping", page.TextUri, page.Id);
            return new FetchedPage(page, xml, null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch page {CanvasId} from {Url}", page.Id, page.TextUri);
            return new FetchedPage(page, null, null, $"{page.Id}: {ex.Message}");
        }
    }
}
