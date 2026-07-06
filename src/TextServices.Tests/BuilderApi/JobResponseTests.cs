using Shouldly;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Storage;

namespace TextServices.Tests.BuilderApi;

public class JobResponseTests
{
    private static TextServicesOptions Options(string baseUrl = "https://search.example.org") =>
        new() { SearchApiBaseUrl = baseUrl };

    private static BuilderJob CompletedJob(
        JobServices services = JobServices.All,
        int? fulfilledServices = null) =>
        new()
        {
            Id = "test/job",
            Services = (int)services,
            FulfilledServices = fulfilledServices,
            Status = JobStatus.Completed,
        };

    // -------------------------------------------------------------------------
    // InvocationCount — reflects the job's current run count
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NewJob_InvocationCountIsOne()
    {
        var response = JobResponse.From(CompletedJob(), Options());
        response.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public void From_ReprocessedJob_InvocationCountReflectsJobValue()
    {
        var job = CompletedJob();
        job.InvocationCount = 3;

        var response = JobResponse.From(job, Options());

        response.InvocationCount.ShouldBe(3);
    }

    // -------------------------------------------------------------------------
    // FulfilledServices field
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NullFulfilledServices_FulfilledServicesFieldIsNull()
    {
        var response = JobResponse.From(CompletedJob(fulfilledServices: null), Options());
        response.FulfilledServices.ShouldBeNull();
    }

    [Fact]
    public void From_FulfilledServicesSet_FulfilledServicesFieldReflectsValue()
    {
        var job = CompletedJob(fulfilledServices: (int)(JobServices.Search | JobServices.Autocomplete));
        var response = JobResponse.From(job, Options());
        response.FulfilledServices.ShouldBe(JobServices.Search | JobServices.Autocomplete);
    }

    // -------------------------------------------------------------------------
    // Endpoint URL generation — fulfilled services
    // -------------------------------------------------------------------------

    [Fact]
    public void From_CompletedJob_AllFulfilled_AllEndpointUrlsPopulated()
    {
        var allFulfilled = JobServices.Search | JobServices.Autocomplete | JobServices.FullText |
                           JobServices.Annotations | JobServices.Pdf | JobServices.TextAugmented |
                           JobServices.Figures;
        var response = JobResponse.From(CompletedJob(fulfilledServices: (int)allFulfilled), Options());

        response.SearchV1.ShouldBe("https://search.example.org/search/v1/test/job");
        response.SearchV2.ShouldBe("https://search.example.org/search/v2/test/job");
        response.AutocompleteV1.ShouldBe("https://search.example.org/autocomplete/v1/test/job");
        response.AutocompleteV2.ShouldBe("https://search.example.org/autocomplete/v2/test/job");
        response.FullText.ShouldBe("https://search.example.org/text/v1/test/job");
        response.Pdf.ShouldBe("https://search.example.org/pdf/v1/test/job");
        response.TextAugmented.ShouldBe("https://search.example.org/text-augmented/v3/test/job");
        response.Annotations.ShouldBe("https://search.example.org/annotations/manifest/v1/test/job");
        response.Figures.ShouldBe("https://search.example.org/identified/figures/test/job");
    }

    [Fact]
    public void From_CompletedJob_FulfilledServicesZero_AllEndpointUrlsNull()
    {
        // Job ran but produced nothing (e.g. manifest had no text).
        var response = JobResponse.From(CompletedJob(fulfilledServices: (int)JobServices.None), Options());

        response.SearchV1.ShouldBeNull();
        response.SearchV2.ShouldBeNull();
        response.AutocompleteV1.ShouldBeNull();
        response.AutocompleteV2.ShouldBeNull();
        response.FullText.ShouldBeNull();
        response.Pdf.ShouldBeNull();
        response.TextAugmented.ShouldBeNull();
        response.Annotations.ShouldBeNull();
        response.Figures.ShouldBeNull();
    }

    [Fact]
    public void From_CompletedJob_PartialFulfillment_OnlyFulfilledUrlsPopulated()
    {
        // All services requested but only Search + Autocomplete were fulfilled.
        var job = CompletedJob(
            services: JobServices.All,
            fulfilledServices: (int)(JobServices.Search | JobServices.Autocomplete));

        var response = JobResponse.From(job, Options());

        response.SearchV1.ShouldNotBeNull();
        response.SearchV2.ShouldNotBeNull();
        response.AutocompleteV1.ShouldNotBeNull();
        response.AutocompleteV2.ShouldNotBeNull();
        response.FullText.ShouldBeNull();
        response.Pdf.ShouldBeNull();
        response.TextAugmented.ShouldBeNull();
        response.Annotations.ShouldBeNull();
        response.Figures.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Backward compatibility — null FulfilledServices falls back to Services
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NullFulfilledServices_FallsBackToServicesForUrlGeneration()
    {
        // Pre-existing job with no FulfilledServices; URLs derived from Services.
        var job = CompletedJob(
            services: JobServices.Search | JobServices.Autocomplete,
            fulfilledServices: null);

        var response = JobResponse.From(job, Options());

        response.SearchV1.ShouldNotBeNull();
        response.AutocompleteV1.ShouldNotBeNull();
        response.FullText.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Non-completed jobs — no endpoint URLs regardless of fulfilled
    // -------------------------------------------------------------------------

    [Fact]
    public void From_RunningJob_NoEndpointUrls()
    {
        var job = new BuilderJob
        {
            Id = "test/running",
            Services = (int)JobServices.All,
            FulfilledServices = null,
            Status = JobStatus.Running,
        };

        var response = JobResponse.From(job, Options());

        response.SearchV1.ShouldBeNull();
        response.AutocompleteV1.ShouldBeNull();
        response.FullText.ShouldBeNull();
    }

    [Fact]
    public void From_FailedJob_NoEndpointUrls()
    {
        var job = new BuilderJob
        {
            Id = "test/failed",
            Services = (int)JobServices.All,
            FulfilledServices = (int)JobServices.None,
            Status = JobStatus.Failed,
        };

        var response = JobResponse.From(job, Options());

        response.SearchV1.ShouldBeNull();
        response.FulfilledServices.ShouldBe(JobServices.None);
    }

    // -------------------------------------------------------------------------
    // No SearchApiBaseUrl — no endpoint URLs
    // -------------------------------------------------------------------------

    [Fact]
    public void From_NoSearchApiBaseUrl_NoEndpointUrls()
    {
        var allFulfilled = (int)(JobServices.Search | JobServices.Autocomplete);
        var response = JobResponse.From(CompletedJob(fulfilledServices: allFulfilled), new TextServicesOptions());

        response.SearchV1.ShouldBeNull();
        response.AutocompleteV1.ShouldBeNull();
    }
}
