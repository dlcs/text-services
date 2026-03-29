using Hangfire;
using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Jobs;

/// <summary>
/// Hangfire background job that processes a single text-build request.
/// Full processing is implemented in PR 5/6; this stub updates job status
/// so the infrastructure can be verified end-to-end.
/// </summary>
public class TextBuildJob(BuilderDbContext db, ILogger<TextBuildJob> logger)
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

        logger.LogInformation("TextBuildJob started for {JobId} — full processing not yet implemented", jobId);

        // TODO: IIIF Manifest fetch + ALTO parsing implemented in PR 5/6.

        job.Status   = JobStatus.Completed;
        job.Finished = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
