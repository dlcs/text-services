using System.Text.Json.Nodes;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Search.Api.Features.TextAugmented;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

public class TextAugmentedHandlerTests
{
    private const string SelfUrl       = "https://search.example.org/text-augmented/v3/test/book";
    private const string SearchBase    = "https://search.example.org";
    private const string ExpectedSearchV2       = "https://search.example.org/search/v2/test/book";
    private const string ExpectedAutocompleteV2 = "https://search.example.org/autocomplete/v2/test/book";
    private const string ExpectedSearchV1       = "https://search.example.org/search/v1/test/book";
    private const string ExpectedAutocompleteV1 = "https://search.example.org/autocomplete/v1/test/book";

    // -------------------------------------------------------------------------
    // Not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ManifestNotFound_ReturnsNull()
    {
        var handler = MakeHandler(null);

        var result = await handler.Handle(
            new TextAugmentedRequest("missing/book", SelfUrl, SearchBase), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // @id replacement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_V3Manifest_ReplacesIdWithSelfUrl()
    {
        var handler = MakeHandler(V3Manifest("https://original.example.org/manifest/1"));

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        result.ShouldNotBeNull();
        result!["id"]!.GetValue<string>().ShouldBe(SelfUrl);
    }

    [Fact]
    public async Task Handle_V2Manifest_ReplacesAtIdWithSelfUrl()
    {
        var handler = MakeHandler(V2Manifest("https://original.example.org/manifest/1"));

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        result.ShouldNotBeNull();
        result!["@id"]!.GetValue<string>().ShouldBe(SelfUrl);
    }

    // -------------------------------------------------------------------------
    // Service injection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_NoExistingService_CreatesTwoServiceEntries()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(2); // v2 + v1
    }

    [Fact]
    public async Task Handle_ExistingServiceArray_PrependsSearchServices()
    {
        var handler = MakeHandler(V3ManifestWithServiceArray());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(3); // v2 + v1 + original
    }

    [Fact]
    public async Task Handle_ExistingServiceObject_PromotesToArrayWithSearchFirst()
    {
        var handler = MakeHandler(V3ManifestWithServiceObject());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(3); // v2 + v1 + original
        service[0]!["id"]!.GetValue<string>().ShouldBe(ExpectedSearchV2); // v2 is first
    }

    [Fact]
    public async Task Handle_ExistingServiceArray_SearchServicesAreFirst()
    {
        var handler = MakeHandler(V3ManifestWithServiceArray());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service[0]!["id"]!.GetValue<string>().ShouldBe(ExpectedSearchV2);
        service[1]!["id"]!.GetValue<string>().ShouldBe(ExpectedSearchV1);
        service[2]!["@id"]!.GetValue<string>().ShouldBe("https://example.org/other"); // original pushed down
    }

    [Fact]
    public async Task Handle_SearchServiceV2_HasCorrectTypeAndId()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var searchServiceV2 = result!["service"]![0]!;
        searchServiceV2["id"]!.GetValue<string>().ShouldBe(ExpectedSearchV2);
        searchServiceV2["type"]!.GetValue<string>().ShouldBe("SearchService2");
    }

    [Fact]
    public async Task Handle_SearchServiceV1_HasCorrectTypeAndId()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var searchServiceV1 = result!["service"]![1]!;
        searchServiceV1["id"]!.GetValue<string>().ShouldBe(ExpectedSearchV1);
        searchServiceV1["type"]!.GetValue<string>().ShouldBe("SearchService1");
    }

    [Fact]
    public async Task Handle_AutocompleteServiceV2_NestedInsideSearchServiceV2()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var autocomplete = result!["service"]![0]!["service"]![0]!;
        autocomplete["id"]!.GetValue<string>().ShouldBe(ExpectedAutocompleteV2);
        autocomplete["type"]!.GetValue<string>().ShouldBe("AutoCompleteService2");
    }

    [Fact]
    public async Task Handle_AutocompleteServiceV1_NestedInsideSearchServiceV1()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var autocomplete = result!["service"]![1]!["service"]![0]!;
        autocomplete["id"]!.GetValue<string>().ShouldBe(ExpectedAutocompleteV1);
        autocomplete["type"]!.GetValue<string>().ShouldBe("AutoCompleteService1");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TextAugmentedHandler MakeHandler(string? manifestJson)
        => new(new StubTextStore(manifestJson));

    private static string V3Manifest(string id = "https://original.example.org/manifest/1")
        => $$"""{"id":"{{id}}","type":"Manifest","@context":"http://iiif.io/api/presentation/3/context.json"}""";

    private static string V2Manifest(string id = "https://original.example.org/manifest/1")
        => $$"""{"@id":"{{id}}","@type":"sc:Manifest","@context":"http://iiif.io/api/presentation/2/context.json"}""";

    private static string V3ManifestWithServiceArray()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":[{"@id":"https://example.org/other","profile":"other"}]}""";

    private static string V3ManifestWithServiceObject()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":{"@id":"https://example.org/other","profile":"other"}}""";

    private sealed class StubTextStore(string? manifestJson, string? figuresJson = null) : ITextStore
    {
        public Task<string?> LoadManifest(string key) => Task.FromResult(manifestJson);
        public Task SaveManifest(string key, string json) => Task.CompletedTask;
        public Task SaveText(string key, Text text) => Task.CompletedTask;
        public Task<Text?> LoadText(string key) => Task.FromResult<Text?>(null);
        public Task SaveAutoComplete(string key, AutoComplete ac) => Task.CompletedTask;
        public Task<AutoComplete?> LoadAutoComplete(string key) => Task.FromResult<AutoComplete?>(null);
        public Task SaveFigures(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadFigures(string key) => Task.FromResult(figuresJson);
        public Task<bool> Exists(string key) => Task.FromResult(false);
    }
}
