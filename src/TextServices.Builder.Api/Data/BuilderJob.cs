namespace TextServices.Builder.Api.Data;

/// <summary>
/// EF Core entity that represents a text-build job persisted in PostgreSQL.
/// </summary>
public class BuilderJob
{
    /// <summary>
    /// The job key (e.g. "2/books/my-book"). Used as the primary key and as the
    /// storage key for the resulting artefacts. May contain '/' characters.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>Source IIIF Manifest URI, when the job was submitted with <c>sourceUri</c>.</summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// JSON-serialised array of <see cref="Features.Jobs.PageInstruction"/>, when the job
    /// was submitted with inline <c>sourceData</c>. Null when <see cref="SourceUri"/> is set.
    /// </summary>
    public string? SourceDataJson { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Waiting;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Finished { get; set; }

    public int TotalPages { get; set; }
    public int PagesCompleted { get; set; }
    public int TotalWordCount { get; set; }
    public int TotalImageCount { get; set; }

    /// <summary>Error message(s) accumulated during processing. Null if no errors.</summary>
    public string? Errors { get; set; }

    /// <summary>Hangfire background job ID, stored to allow cancellation.</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>
    /// Bitmask of <see cref="TextServices.Storage.JobServices"/> flags controlling which
    /// derivatives are built and which endpoints are exposed. -1 means all services enabled.
    /// </summary>
    public int Services { get; set; } = -1;

    /// <summary>Optional document title (PDF metadata / Content-Disposition).</summary>
    public string? Title { get; set; }

    /// <summary>
    /// JSON-serialised <c>Dictionary&lt;string, CustomPageType&gt;</c> from the original
    /// job instruction. Null when no custom page types were supplied.
    /// </summary>
    public string? CustomTypesJson { get; set; }

    /// <summary>
    /// Bitmask of <see cref="TextServices.Storage.JobServices"/> flags that were actually
    /// fulfilled during the most recent run. Null until the job has completed processing.
    /// A value of 0 means processing completed but no derivatives could be produced.
    /// </summary>
    public int? FulfilledServices { get; set; }

    /// <summary>
    /// Opaque identifier supplied by the caller when the job was created (e.g. a
    /// pipelineJob GUID). Echoed back in <see cref="Services.Notifications.JobCompletionNotification"/>
    /// so callers can tie a completion notification to the request that created the job.
    /// </summary>
    public string? CorrelationId { get; set; }
}
