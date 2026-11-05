using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Services.Notifications;

/// <param name="Errors">Errors recorded for the job, newline-separated.</param>
/// <param name="SkippedCanvases">See <see cref="BuilderJob.SkippedCanvases"/>.</param>
/// <param name="PagesWithoutTextSource">See <see cref="BuilderJob.PagesWithoutTextSource"/>.</param>
/// <param name="PagesWithoutWords">See <see cref="BuilderJob.PagesWithoutWords"/>.</param>
public record JobCompletionNotification(
    string JobId,
    JobStatus Status,
    DateTimeOffset? Finished,
    int TotalPages,
    int TotalWordCount,
    string? Errors,
    int InvocationCount,
    int SkippedCanvases,
    int PagesWithoutTextSource,
    int PagesWithoutWords)
{
    public static JobCompletionNotification FromJob(BuilderJob job) =>
        new(job.Id, job.Status, job.Finished, job.TotalPages, job.TotalWordCount, job.Errors, job.InvocationCount,
            job.SkippedCanvases, job.PagesWithoutTextSource, job.PagesWithoutWords);
}
