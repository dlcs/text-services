using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Infrastructure;
using TextServices.Storage;

namespace TextServices.Builder.Api.Features.Jobs;

/// <summary>
/// Response body for <c>GET /textbuilder/{**id}</c> and
/// the 202 body for <c>POST /textbuilder</c>.
/// </summary>
public class JobResponse
{
    public required string Id { get; set; }
    public string? SourceUri { get; set; }
    public List<PageInstruction>? SourceData { get; set; }
    public string Status { get; set; } = "Waiting";
    public JobServices Services { get; set; } = JobServices.All;
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Finished { get; set; }
    public int TotalPages { get; set; }
    public int PagesCompleted { get; set; }
    public int TotalWordCount { get; set; }
    public int TotalImageCount { get; set; }
    public string? Errors { get; set; }

    /// <summary>
    /// The services that were actually produced during the most recent run.
    /// Null for jobs processed before this field was introduced.
    /// Use <see cref="Services"/> to see what was requested.
    /// </summary>
    public JobServices? FulfilledServices { get; set; }

    // Endpoint URLs — null when not yet completed or service was not fulfilled.
    public string? SearchV1 { get; set; }
    public string? AutocompleteV1 { get; set; }
    public string? SearchV2 { get; set; }
    public string? AutocompleteV2 { get; set; }
    public string? FullText { get; set; }
    public string? Pdf { get; set; }
    public string? TextAugmented { get; set; }
    public string? Annotations { get; set; }
    public string? Figures { get; set; }

    public static JobResponse From(BuilderJob job, TextServicesOptions options)
    {
        List<PageInstruction>? sourceData = null;
        if (job.SourceDataJson != null)
        {
            sourceData = System.Text.Json.JsonSerializer.Deserialize<List<PageInstruction>>(
                job.SourceDataJson);
        }

        var services = (JobServices)job.Services;

        // Use fulfilled services for URL generation when available; fall back to requested
        // services for jobs processed before FulfilledServices was introduced.
        var fulfilled = job.FulfilledServices.HasValue
            ? (JobServices)job.FulfilledServices.Value
            : services;

        string? searchV1 = null;
        string? autocompleteV1 = null;
        string? searchV2 = null;
        string? autocompleteV2 = null;
        string? fullText = null;
        string? pdf = null;
        string? textAugmented = null;
        string? annotations = null;
        string? figures = null;

        if (job.Status == JobStatus.Completed &&
            !string.IsNullOrEmpty(options.SearchApiBaseUrl))
        {
            var baseUrl = options.SearchApiBaseUrl.TrimEnd('/');

            if (fulfilled.HasFlag(JobServices.Search))
            {
                searchV1 = SearchApiRoutes.SearchV1(baseUrl, job.Id);
                searchV2 = SearchApiRoutes.SearchV2(baseUrl, job.Id);
            }

            if (fulfilled.HasFlag(JobServices.Autocomplete))
            {
                autocompleteV1 = SearchApiRoutes.AutocompleteV1(baseUrl, job.Id);
                autocompleteV2 = SearchApiRoutes.AutocompleteV2(baseUrl, job.Id);
            }

            if (fulfilled.HasFlag(JobServices.FullText))
                fullText = SearchApiRoutes.FullText(baseUrl, job.Id);

            if (fulfilled.HasFlag(JobServices.Pdf))
                pdf = SearchApiRoutes.Pdf(baseUrl, job.Id);

            if (fulfilled.HasFlag(JobServices.TextAugmented))
                textAugmented = SearchApiRoutes.TextAugmented(baseUrl, job.Id);

            if (fulfilled.HasFlag(JobServices.Annotations))
                annotations = SearchApiRoutes.AnnotationsManifest(baseUrl, job.Id);

            if (fulfilled.HasFlag(JobServices.Figures))
                figures = SearchApiRoutes.Figures(baseUrl, job.Id);
        }

        return new JobResponse
        {
            Id = job.Id,
            SourceUri = job.SourceUri,
            SourceData = sourceData,
            Status = job.Status.ToString(),
            Services = services,
            FulfilledServices = job.FulfilledServices.HasValue ? fulfilled : null,
            Created = job.Created,
            Started = job.Started,
            Finished = job.Finished,
            TotalPages = job.TotalPages,
            PagesCompleted = job.PagesCompleted,
            TotalWordCount = job.TotalWordCount,
            TotalImageCount = job.TotalImageCount,
            Errors = job.Errors,
            SearchV1 = searchV1,
            AutocompleteV1 = autocompleteV1,
            SearchV2 = searchV2,
            AutocompleteV2 = autocompleteV2,
            FullText = fullText,
            Pdf = pdf,
            TextAugmented = textAugmented,
            Annotations = annotations,
            Figures = figures,
        };
    }
}
