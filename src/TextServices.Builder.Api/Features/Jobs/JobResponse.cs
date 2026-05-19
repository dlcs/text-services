using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
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

    // Endpoint URLs — null when not yet completed or service is not enabled.
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

            if (services.HasFlag(JobServices.Search))
            {
                searchV1 = $"{baseUrl}/search/v1/{job.Id}";
                searchV2 = $"{baseUrl}/search/v2/{job.Id}";
            }

            if (services.HasFlag(JobServices.Autocomplete))
            {
                autocompleteV1 = $"{baseUrl}/autocomplete/v1/{job.Id}";
                autocompleteV2 = $"{baseUrl}/autocomplete/v2/{job.Id}";
            }

            if (services.HasFlag(JobServices.FullText))
                fullText = $"{baseUrl}/text/v1/{job.Id}";

            if (services.HasFlag(JobServices.Pdf))
                pdf = $"{baseUrl}/pdf/v1/{job.Id}";

            if (services.HasFlag(JobServices.TextAugmented))
                textAugmented = $"{baseUrl}/text-augmented/v3/{job.Id}";

            if (services.HasFlag(JobServices.Annotations))
                annotations = $"{baseUrl}/annotations/manifest/v1/{job.Id}";

            if (services.HasFlag(JobServices.Figures))
                figures = $"{baseUrl}/identified/figures/{job.Id}";
        }

        return new JobResponse
        {
            Id = job.Id,
            SourceUri = job.SourceUri,
            SourceData = sourceData,
            Status = job.Status.ToString(),
            Services = services,
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
