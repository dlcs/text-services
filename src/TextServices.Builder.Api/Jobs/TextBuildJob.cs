using System.Text.Json;
using Hangfire;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;
using TextServices.Storage;

namespace TextServices.Builder.Api.Jobs;

/// <summary>
/// Hangfire background job that processes a single text-build request.
/// PR 5: fetches the IIIF Manifest (when <c>sourceUri</c> was supplied),
///       reduces it to a page sequence, and stores the raw Manifest JSON.
/// PR 6 will add the ALTO-fetching and text-building steps.
/// </summary>
public class TextBuildJob(
    BuilderDbContext db,
    IManifestFetcher manifestFetcher,
    ITextStore textStore,
    ILogger<TextBuildJob> logger)
{
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
            IReadOnlyList<PageInstruction> pages;

            if (job.SourceUri != null)
            {
                logger.LogInformation("Fetching manifest for job {JobId} from {Uri}", jobId, job.SourceUri);

                var result = await manifestFetcher.FetchAndReduce(
                    job.SourceUri, cancellationToken.ShutdownToken);

                await textStore.SaveManifest(job.Id, result.Json);
                job.SourceDataJson = JsonSerializer.Serialize(result.Pages);
                pages = result.Pages;

                logger.LogInformation(
                    "Manifest fetched for {JobId}: {PageCount} canvases", jobId, pages.Count);
            }
            else
            {
                pages = job.SourceDataJson != null
                    ? JsonSerializer.Deserialize<List<PageInstruction>>(job.SourceDataJson) ?? []
                    : [];
            }

            job.TotalPages = pages.Count;
            await db.SaveChangesAsync();

            // TODO: Fetch ALTO files and build Text artefacts — implemented in PR 6.

            job.Status   = JobStatus.Completed;
            job.Finished = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "TextBuildJob completed for {JobId} ({PageCount} pages, processing stub)",
                jobId, pages.Count);
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
}
