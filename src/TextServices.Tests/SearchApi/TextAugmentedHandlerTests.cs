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
    private const string ExpectedSearch       = "https://search.example.org/search/v1/test/book";
    private const string ExpectedAutocomplete = "https://search.example.org/autocomplete/v1/test/book";

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
    public async Task Handle_NoExistingService_CreatesServiceArray()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ExistingServiceArray_AppendsSearchService()
    {
        var handler = MakeHandler(V3ManifestWithServiceArray());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(2); // original + search
    }

    [Fact]
    public async Task Handle_ExistingServiceObject_PromotesToArrayWithSearchFirst()
    {
        var handler = MakeHandler(V3ManifestWithServiceObject());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(2);
        service[0]!["@id"]!.GetValue<string>().ShouldBe(ExpectedSearch); // search service is first
    }

    [Fact]
    public async Task Handle_ExistingServiceArray_SearchServiceIsFirst()
    {
        var handler = MakeHandler(V3ManifestWithServiceArray());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service[0]!["@id"]!.GetValue<string>().ShouldBe(ExpectedSearch);
        service[1]!["@id"]!.GetValue<string>().ShouldBe("https://example.org/other"); // original pushed down
    }

    [Fact]
    public async Task Handle_SearchService_HasCorrectProfileAndId()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var searchService = result!["service"]![0]!;
        searchService["@id"]!.GetValue<string>().ShouldBe(ExpectedSearch);
        searchService["profile"]!.GetValue<string>().ShouldBe("http://iiif.io/api/search/1/search");
    }

    [Fact]
    public async Task Handle_AutocompleteService_NestedInsideSearchService()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var autocomplete = result!["service"]![0]!["service"]!;
        autocomplete["@id"]!.GetValue<string>().ShouldBe(ExpectedAutocomplete);
        autocomplete["profile"]!.GetValue<string>().ShouldBe("http://iiif.io/api/search/1/autocomplete");
    }

    [Fact]
    public async Task Handle_SearchService_HasCorrectContext()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var searchService = result!["service"]![0]!;
        searchService["@context"]!.GetValue<string>().ShouldBe("http://iiif.io/api/search/1/context.json");
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

    private sealed class StubTextStore(string? manifestJson) : ITextStore
    {
        public Task<string?> LoadManifest(string key) => Task.FromResult(manifestJson);
        public Task SaveManifest(string key, string json) => Task.CompletedTask;
        public Task SaveText(string key, Text text) => Task.CompletedTask;
        public Task<Text?> LoadText(string key) => Task.FromResult<Text?>(null);
        public Task SaveAutoComplete(string key, AutoComplete ac) => Task.CompletedTask;
        public Task<AutoComplete?> LoadAutoComplete(string key) => Task.FromResult<AutoComplete?>(null);
        public Task<bool> Exists(string key) => Task.FromResult(false);
    }
}
