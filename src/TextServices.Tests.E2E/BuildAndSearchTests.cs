using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using TextServices.Storage;
using TextServices.Tests.E2E.Infrastructure;

namespace TextServices.Tests.E2E;

/// <summary>
/// End-to-end tests for the full pipeline: Builder API → stored artefacts → Search API.
///
/// Uses <see cref="E2ETestContext"/> which boots both APIs in-process with:
/// - EF Core PostgreSQL (TestContainers — real Postgres, migrations applied)
/// - Hangfire InMemory (jobs processed by a real background server in the test process)
/// - <see cref="FixtureAltoFetcher"/> + <see cref="FixtureManifestFetcher"/> (local fixture files)
/// - Shared temp <see cref="FileSystemTextStore"/> (Builder writes, Search reads)
/// </summary>
[Collection("E2E")]
public class BuildAndSearchTests(E2ETestContext ctx)
{
    // -------------------------------------------------------------------------
    // Builder API — job lifecycle
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostJob_Returns202WithLocation()
    {
        var response = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id = "e2e/lifecycle-test",
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldContain("e2e/lifecycle-test");
    }

    [Fact]
    public async Task PostJob_IdStartingWithSlash_Returns400()
    {
        var response = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id = "/bad-id",
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostJob_IdWithInternalSlashes_Returns202()
    {
        var response = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id = "e2e/slash/in/id",
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task PostJob_DuplicateId_Returns409()
    {
        var id = "e2e/duplicate-test";
        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        var second = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetJob_Unknown_Returns404()
    {
        var response = await ctx.BuilderClient.GetAsync("/textbuilder/no/such/job");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // Builder API — full pipeline (with ALTO)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildJob_WithAltoManifest_CompletesSuccessfully()
    {
        var id = "e2e/b2888193x-full";

        var post = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });
        post.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));

        body.ShouldContain("\"Completed\"");
        body.ShouldNotContain("\"Failed\"");
    }

    [Fact]
    public async Task BuildJob_CompletedJob_HasWordAndPageCounts()
    {
        var id = "e2e/b2888193x-counts";

        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        var body = await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));
        var json = JsonNode.Parse(body)!;

        json["totalPages"]!.GetValue<int>().ShouldBe(16);
        json["totalWordCount"]!.GetValue<int>().ShouldBeGreaterThan(0);
        json["totalImageCount"]!.GetValue<int>().ShouldBe(16);
    }

    // -------------------------------------------------------------------------
    // Builder API — manifest with no ALTO (all sparse pages)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildJob_NoAltoManifest_CompletesWithZeroWords()
    {
        var id = "e2e/b28770997-noalto";

        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b28770997"
        });

        var body = await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(30));
        var json = JsonNode.Parse(body)!;

        body.ShouldContain("\"Completed\"");
        json["totalWordCount"]!.GetValue<int>().ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Search API — querying completed jobs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Search_KnownWord_ReturnsHits()
    {
        var id = await BuildFixtureAsync("e2e/search-hits");

        var response = await ctx.SearchClient.GetAsync($"/search/v1/{id}?q=health");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["@type"]!.GetValue<string>().ShouldBe("sc:AnnotationList");
        body["resources"]!.AsArray().Count.ShouldBeGreaterThan(0);
        body["hits"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmptyNotError()
    {
        var id = await BuildFixtureAsync("e2e/search-empty");

        var response = await ctx.SearchClient.GetAsync($"/search/v1/{id}?q=");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["resources"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task Search_UnknownId_Returns404()
    {
        var response = await ctx.SearchClient.GetAsync("/search/v1/no/such/id?q=test");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // Autocomplete API
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Autocomplete_ShortQuery_ReturnsEmptyTermList()
    {
        var id = await BuildFixtureAsync("e2e/ac-short");

        var response = await ctx.SearchClient.GetAsync($"/autocomplete/v1/{id}?q=he");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["@type"]!.GetValue<string>().ShouldBe("search:TermList");
        body["terms"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task Autocomplete_MatchingPrefix_ReturnsSuggestions()
    {
        var id = await BuildFixtureAsync("e2e/ac-match");

        var response = await ctx.SearchClient.GetAsync($"/autocomplete/v1/{id}?q=hea");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["terms"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Text-augmented endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TextAugmented_CompletedJob_ReturnsManifestWithSearchService()
    {
        var id = await BuildFixtureAsync("e2e/text-augmented");

        var response = await ctx.SearchClient.GetAsync($"/text-augmented/v3/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        // id should be replaced with the text-augmented URL
        body["id"]!.GetValue<string>().ShouldContain("text-augmented");

        // Search service v2 should be first, v1 second
        var service = body["service"]!.AsArray();
        service.Count.ShouldBeGreaterThanOrEqualTo(2);
        service[0]!["type"]!.GetValue<string>().ShouldBe("SearchService2");
        service[1]!["type"]!.GetValue<string>().ShouldBe("SearchService1");
    }

    [Fact]
    public async Task TextAugmented_UnknownId_Returns404()
    {
        var response = await ctx.SearchClient.GetAsync("/text-augmented/v3/no/such/id");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Builder API — sourceData (inline page sequence) path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostJob_WithSourceData_Returns202WithLocation()
    {
        var response = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id = "e2e/sd-lifecycle",
            sourceData = SourceDataPages()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldContain("e2e/sd-lifecycle");
    }

    [Fact]
    public async Task BuildJob_WithSourceData_CompletesSuccessfully()
    {
        var id = "e2e/sd-completes";

        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceData = SourceDataPages()
        });

        var body = await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));

        body.ShouldContain("\"Completed\"");
        body.ShouldNotContain("\"Failed\"");
    }

    [Fact]
    public async Task BuildJob_WithSourceData_HasCorrectPageAndWordCounts()
    {
        var id = "e2e/sd-counts";

        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceData = SourceDataPages()
        });

        var body = await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));
        var json = JsonNode.Parse(body)!;

        json["totalPages"]!.GetValue<int>().ShouldBe(4);
        json["totalWordCount"]!.GetValue<int>().ShouldBeGreaterThan(0);
        json["totalImageCount"]!.GetValue<int>().ShouldBe(4);
    }

    [Fact]
    public async Task Search_AfterSourceDataBuild_ReturnsHits()
    {
        var id = await BuildSourceDataFixtureAsync("e2e/sd-search-hits");

        var response = await ctx.SearchClient.GetAsync($"/search/v1/{id}?q=annual");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["@type"]!.GetValue<string>().ShouldBe("sc:AnnotationList");
        body["resources"]!.AsArray().Count.ShouldBeGreaterThan(0);
        body["hits"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Autocomplete_AfterSourceDataBuild_ReturnsSuggestions()
    {
        var id = await BuildSourceDataFixtureAsync("e2e/sd-autocomplete");

        var response = await ctx.SearchClient.GetAsync($"/autocomplete/v1/{id}?q=ann");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["@type"]!.GetValue<string>().ShouldBe("search:TermList");
        body["terms"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task TextAugmented_AfterSourceDataBuild_ReturnsSyntheticManifest()
    {
        // sourceData jobs synthesise a skeleton manifest, so text-augmented returns 200.
        var id = await BuildSourceDataFixtureAsync("e2e/sd-text-augmented");

        var response = await ctx.SearchClient.GetAsync($"/text-augmented/v3/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["type"]!.GetValue<string>().ShouldBe("Manifest");
        // Search service descriptors should have been injected.
        body["service"].ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // Service flags — capabilities gating roundtrip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostJob_WithRestrictedServices_ResponseReflectsServicesField()
    {
        const int searchAndTextAugmented = (int)(JobServices.Search | JobServices.TextAugmented);
        var id = "e2e/flags-response";

        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceData = SourceDataPages(),
            services = searchAndTextAugmented,
        });

        var body = await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));
        var json = JsonNode.Parse(body)!;

        // The services field must round-trip the posted value.
        json["services"].ShouldNotBeNull();
        var services = (JobServices)json["services"]!.GetValue<int>();
        services.HasFlag(JobServices.Search).ShouldBeTrue();
        services.HasFlag(JobServices.TextAugmented).ShouldBeTrue();
        services.HasFlag(JobServices.Autocomplete).ShouldBeFalse();
    }

    [Fact]
    public async Task ServiceFlags_SearchEnabled_AutocompleteDisabled_GatesEndpoints()
    {
        // Build with Search only — capabilities file gates Autocomplete.
        const int searchOnly = (int)JobServices.Search;
        var id = "e2e/flags-gating";

        await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceData = SourceDataPages(),
            services = searchOnly,
        });

        await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));

        // Search endpoint must work.
        var searchResponse = await ctx.SearchClient.GetAsync($"/search/v1/{id}?q=annual");
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Autocomplete endpoint must be gated (404).
        var acResponse = await ctx.SearchClient.GetAsync($"/autocomplete/v1/{id}?q=ann");
        acResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // Route safety — ID is required on all mutation / resource endpoints
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/textbuilder/", "DELETE")]
    [InlineData("/textbuilder/", "PUT")]
    public async Task BuilderApi_MutationWithoutId_Returns404(string path, string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await ctx.BuilderClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/search/v1/")]
    [InlineData("/search/v2/")]
    [InlineData("/autocomplete/v1/")]
    [InlineData("/autocomplete/v2/")]
    [InlineData("/text-augmented/v3/")]
    [InlineData("/text/v1/")]
    [InlineData("/annotations/manifest/v1/")]
    public async Task SearchApi_ResourceEndpointWithoutId_Returns404(string path)
    {
        var response = await ctx.SearchClient.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Submits a job for b2888193x under <paramref name="id"/> and waits for completion.
    /// Returns the job ID. Results are shared via the same temp storage, so tests that
    /// don't care about which job was built can reuse this helper.
    /// </summary>
    private async Task<string> BuildFixtureAsync(string id)
    {
        var post = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceUri = "https://iiif.wellcomecollection.org/presentation/b2888193x"
        });

        // 409 Conflict is fine — the job already exists (test reuse).
        post.StatusCode.ShouldBeOneOf(HttpStatusCode.Accepted, HttpStatusCode.Conflict);

        await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));
        return id;
    }

    /// <summary>
    /// Submits a 4-page sourceData job under <paramref name="id"/> and waits for
    /// completion. Uses the first 4 canvases of b2888193x, whose ALTO files are
    /// already checked in under Fixtures/b2888193x/alto/.
    /// </summary>
    private async Task<string> BuildSourceDataFixtureAsync(string id)
    {
        var post = await ctx.BuilderClient.PostAsJsonAsync("/textbuilder", new
        {
            id,
            sourceData = SourceDataPages()
        });

        post.StatusCode.ShouldBeOneOf(HttpStatusCode.Accepted, HttpStatusCode.Conflict);

        await ctx.WaitForJobAsync(id, TimeSpan.FromSeconds(60));
        return id;
    }

    /// <summary>
    /// Returns the first 4 canvases of b2888193x as an inline sourceData page sequence.
    /// Dimensions and ALTO URLs are taken directly from the b2888193x manifest; the ALTO
    /// files are served by <see cref="FixtureAltoFetcher"/> from the checked-in fixtures.
    /// </summary>
    private static object[] SourceDataPages() =>
    [
        new { id = "https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0001.jp2", width = 2679, height = 4179, textUri = "https://api.wellcomecollection.org/text/alto/b2888193x/b2888193x_0001.jp2" },
        new { id = "https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0002.jp2", width = 2495, height = 4067, textUri = "https://api.wellcomecollection.org/text/alto/b2888193x/b2888193x_0002.jp2" },
        new { id = "https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0003.jp2", width = 2495, height = 4067, textUri = "https://api.wellcomecollection.org/text/alto/b2888193x/b2888193x_0003.jp2" },
        new { id = "https://iiif.wellcomecollection.org/presentation/b2888193x/canvases/b2888193x_0004.jp2", width = 2495, height = 4067, textUri = "https://api.wellcomecollection.org/text/alto/b2888193x/b2888193x_0004.jp2" },
    ];
}
