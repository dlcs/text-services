using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Search.Api.Features.TextAugmented;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

public class TextAugmentedHandlerTests
{
    private const string SelfUrl = "https://search.example.org/text-augmented/v3/test/book";
    private const string SearchBase = "https://search.example.org";
    private const string ExpectedSearchV2 = "https://search.example.org/search/v2/test/book";
    private const string ExpectedAutocompleteV2 = "https://search.example.org/autocomplete/v2/test/book";
    private const string ExpectedSearchV1 = "https://search.example.org/search/v1/test/book";
    private const string ExpectedAutocompleteV1 = "https://search.example.org/autocomplete/v1/test/book";
    private const string ExpectedRawTextUrl = "https://search.example.org/text/v1/test/book";
    private const string ExpectedPdfUrl = "https://search.example.org/pdf/v1/test/book";

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

    [Fact]
    public async Task Handle_NoText_ReturnsManifestWithoutSearchServicesOrContext()
    {
        var handler = MakeHandler(V3Manifest(), noText: true);

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        result.ShouldNotBeNull();
        result["service"].ShouldBeNull();
        result["@context"]!.GetValue<string>().ShouldBe("http://iiif.io/api/presentation/3/context.json");
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
    public async Task Handle_SearchServiceV1_HasCorrectTypeProfileAndId()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var searchServiceV1 = result!["service"]![1]!;
        searchServiceV1["id"]!.GetValue<string>().ShouldBe(ExpectedSearchV1);
        searchServiceV1["type"]!.GetValue<string>().ShouldBe("SearchService1");
        searchServiceV1["profile"]!.GetValue<string>().ShouldBe("http://iiif.io/api/search/1/search");
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
        autocomplete["profile"]!.GetValue<string>().ShouldBe("http://iiif.io/api/search/1/autocomplete");
    }

    // -------------------------------------------------------------------------
    // Rendering links (PDF + plain text)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ImageBasedContent_AddsPdfAndPlainTextRenderingLinks()
    {
        var handler = MakeHandler(V3Manifest(), cachedText: SpatialText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count.ShouldBe(2);
        rendering[0]!["id"]!.GetValue<string>().ShouldBe(ExpectedPdfUrl);
        rendering[0]!["format"]!.GetValue<string>().ShouldBe("application/pdf");
        rendering[1]!["id"]!.GetValue<string>().ShouldBe(ExpectedRawTextUrl);
        rendering[1]!["format"]!.GetValue<string>().ShouldBe("text/plain");
    }

    [Fact]
    public async Task Handle_TemporalOnlyContent_AddsPlainTextButNoPdfLink()
    {
        var handler = MakeHandler(V3Manifest(), cachedText: TemporalText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count.ShouldBe(1);
        rendering[0]!["id"]!.GetValue<string>().ShouldBe(ExpectedRawTextUrl);
        rendering[0]!["format"]!.GetValue<string>().ShouldBe("text/plain");
    }

    [Fact]
    public async Task Handle_TextNotBuilt_NoRenderingLinks()
    {
        var handler = MakeHandler(V3Manifest(), noText: true);

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        result!["rendering"].ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ImageBasedContent_RenderingLinksArePrependedToExisting()
    {
        var handler = MakeHandler(V3ManifestWithRendering(), cachedText: SpatialText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count.ShouldBe(3); // PDF + plain text + original
        rendering[0]!["id"]!.GetValue<string>().ShouldBe(ExpectedPdfUrl);
        rendering[1]!["id"]!.GetValue<string>().ShouldBe(ExpectedRawTextUrl);
    }

    [Fact]
    public async Task Handle_TemporalOnlyContent_PlainTextLinkPrependedToExisting()
    {
        var handler = MakeHandler(V3ManifestWithRendering(), cachedText: TemporalText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count.ShouldBe(2); // plain text + original (no PDF)
        rendering[0]!["id"]!.GetValue<string>().ShouldBe(ExpectedRawTextUrl);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Manifest-level annotations reference
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithAnnotations_InjectsManifestAnnotationsRef()
    {
        const string annotationsJson = """{"type":"AnnotationPage","items":[]}""";
        var handler = MakeHandler(V3Manifest(), annotationsJson: annotationsJson);

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var annotations = result!["annotations"].ShouldBeOfType<JsonArray>();
        var ref0 = annotations[0]!;
        ref0["id"]!.GetValue<string>()
            .ShouldBe("https://search.example.org/annotations/manifest/v1/test/book");
        ref0["type"]!.GetValue<string>().ShouldBe("AnnotationPage");
        ref0["profile"]!.GetValue<string>().ShouldBe("https://dlcs.io/profiles/all-text");
        ref0["label"]!["en"]![0]!.GetValue<string>().ShouldBe("Text of all canvases");
    }

    [Fact]
    public async Task Handle_WithAnnotationsAndFigures_AnnotationsRefBeforeFiguresRef()
    {
        const string annotationsJson = """{"type":"AnnotationPage","items":[]}""";
        const string figuresJson = """{"type":"AnnotationPage","items":[]}""";
        var handler = MakeHandler(V3Manifest(), annotationsJson: annotationsJson, figuresJson: figuresJson);

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var annotations = result!["annotations"].ShouldBeOfType<JsonArray>();
        annotations.Count.ShouldBe(2);
        annotations[0]!["id"]!.GetValue<string>().ShouldContain("annotations/manifest");
        annotations[1]!["id"]!.GetValue<string>().ShouldContain("identified/figures");
    }

    [Fact]
    public async Task Handle_WithoutAnnotations_NoManifestAnnotationsRef()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        result!["annotations"].ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Service deduplication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ServiceArrayAlreadyContainsSearchV2_DoesNotDuplicateV2()
    {
        var handler = MakeHandler(V3ManifestWithExistingSearchV2());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(2); // original v2 stays; v1 added; no duplicate v2
        service.Count(n => n?["id"]?.GetValue<string>() == ExpectedSearchV2).ShouldBe(1);
        service.Count(n => n?["id"]?.GetValue<string>() == ExpectedSearchV1).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ServiceArrayAlreadyContainsBothSearchServices_NeitherDuplicated()
    {
        var handler = MakeHandler(V3ManifestWithBothSearchServices());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(2); // unchanged — both already present
        service.Count(n => n?["id"]?.GetValue<string>() == ExpectedSearchV2).ShouldBe(1);
        service.Count(n => n?["id"]?.GetValue<string>() == ExpectedSearchV1).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ServiceObjectMatchesSearchV2_PromotesToArrayWithoutDuplicatingV2()
    {
        var handler = MakeHandler(V3ManifestWithSearchV2ServiceObject());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var service = result!["service"].ShouldBeOfType<JsonArray>();
        service.Count.ShouldBe(2); // v1 added; original v2 kept; no duplicate v2
        service.Count(n => n?["id"]?.GetValue<string>() == ExpectedSearchV2).ShouldBe(1);
        service.Count(n => n?["id"]?.GetValue<string>() == ExpectedSearchV1).ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Rendering deduplication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_RenderingArrayAlreadyContainsTextRef_DoesNotDuplicateTextRef()
    {
        var handler = MakeHandler(V3ManifestWithExistingTextRef(), cachedText: SpatialText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count(n => n?["id"]?.GetValue<string>() == ExpectedRawTextUrl).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RenderingArrayAlreadyContainsPdfRef_DoesNotDuplicatePdfRef()
    {
        var handler = MakeHandler(V3ManifestWithExistingPdfRef(), cachedText: SpatialText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count(n => n?["id"]?.GetValue<string>() == ExpectedPdfUrl).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_RenderingArrayAlreadyContainsBothRefs_NeitherDuplicated()
    {
        var handler = MakeHandler(V3ManifestWithExistingPdfAndTextRefs(), cachedText: SpatialText());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var rendering = result!["rendering"].ShouldBeOfType<JsonArray>();
        rendering.Count.ShouldBe(2); // unchanged
        rendering.Count(n => n?["id"]?.GetValue<string>() == ExpectedPdfUrl).ShouldBe(1);
        rendering.Count(n => n?["id"]?.GetValue<string>() == ExpectedRawTextUrl).ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Annotations deduplication
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_AnnotationsArrayAlreadyContainsAnnotationsRef_DoesNotDuplicate()
    {
        const string annotationsJson = """{"type":"AnnotationPage","items":[]}""";
        var handler = MakeHandler(V3ManifestWithExistingAnnotationsRef(), annotationsJson: annotationsJson);

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var annotations = result!["annotations"].ShouldBeOfType<JsonArray>();
        annotations.Count(n => n?["id"]?.GetValue<string>() ==
            "https://search.example.org/annotations/manifest/v1/test/book").ShouldBe(1);
    }

    [Fact]
    public async Task Handle_AnnotationsArrayAlreadyContainsFiguresRef_DoesNotDuplicate()
    {
        const string figuresJson = """{"type":"AnnotationPage","items":[]}""";
        var handler = MakeHandler(V3ManifestWithExistingFiguresRef(), figuresJson: figuresJson);

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var annotations = result!["annotations"].ShouldBeOfType<JsonArray>();
        annotations.Count(n => n?["id"]?.GetValue<string>() ==
            "https://search.example.org/identified/figures/test/book").ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // @context injection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_NoContext_AddsSearchContexts()
    {
        var handler = MakeHandler("""{"id":"https://example.org/m/1","type":"Manifest"}""");

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var ctx = result!["@context"].ShouldBeOfType<JsonArray>();
        ctx.Select(n => n!.GetValue<string>()).ShouldBe(
        [
            "http://iiif.io/api/search/2/context.json",
            "http://iiif.io/api/search/1/context.json",
        ]);
    }

    [Fact]
    public async Task Handle_StringContext_PromotesToArrayAndAppendsSearchContexts()
    {
        var handler = MakeHandler(V3Manifest());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var ctx = result!["@context"].ShouldBeOfType<JsonArray>();
        ctx.Select(n => n!.GetValue<string>()).ShouldBe(
        [
            "http://iiif.io/api/presentation/3/context.json",
            "http://iiif.io/api/search/2/context.json",
            "http://iiif.io/api/search/1/context.json",
        ]);
    }

    [Fact]
    public async Task Handle_ArrayContext_AppendsSearchContexts()
    {
        var handler = MakeHandler("""{"id":"https://example.org/m/1","type":"Manifest","@context":["http://iiif.io/api/presentation/3/context.json"]}""");

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var ctx = result!["@context"].ShouldBeOfType<JsonArray>();
        ctx.Select(n => n!.GetValue<string>()).ShouldBe(
        [
            "http://iiif.io/api/presentation/3/context.json",
            "http://iiif.io/api/search/2/context.json",
            "http://iiif.io/api/search/1/context.json",
        ]);
    }

    [Fact]
    public async Task Handle_ContextAlreadyContainsSearchUrls_DoesNotDuplicate()
    {
        var handler = MakeHandler(V3ManifestWithSearchContexts());

        var result = await handler.Handle(
            new TextAugmentedRequest("test/book", SelfUrl, SearchBase), CancellationToken.None);

        var ctx = result!["@context"].ShouldBeOfType<JsonArray>();
        ctx.Count(n => n!.GetValue<string>() == "http://iiif.io/api/search/2/context.json").ShouldBe(1);
        ctx.Count(n => n!.GetValue<string>() == "http://iiif.io/api/search/1/context.json").ShouldBe(1);
    }

    private static TextAugmentedHandler MakeHandler(
        string? manifestJson,
        string? annotationsJson = null,
        string? figuresJson = null,
        Text? cachedText = null,
        bool noText = false)
        => new(new StubTextStore(manifestJson, annotationsJson, figuresJson),
            new StubTextCache(noText ? null : (cachedText ?? SpatialText())),
            new NullLogger<TextAugmentedHandler>());

    /// <summary>A Text with one spatial (image-based) canvas.</summary>
    private static Text SpatialText()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("canvas/1", isTemporalContent: false);
        acc.AddWord("hello", "hello", 10, 20, 50, 12);
        return acc.Build().Text;
    }

    /// <summary>A Text with one temporal (VTT/video) canvas only.</summary>
    private static Text TemporalText()
    {
        var acc = new TextAccumulator();
        acc.BeginPage("canvas/1", isTemporalContent: true);
        acc.AddWord("hello", "hello", 0, 5000);
        return acc.Build().Text;
    }

    private static string V3Manifest(string id = "https://original.example.org/manifest/1")
        => $$"""{"id":"{{id}}","type":"Manifest","@context":"http://iiif.io/api/presentation/3/context.json"}""";

    private static string V2Manifest(string id = "https://original.example.org/manifest/1")
        => $$"""{"@id":"{{id}}","@type":"sc:Manifest","@context":"http://iiif.io/api/presentation/2/context.json"}""";

    private static string V3ManifestWithServiceArray()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":[{"@id":"https://example.org/other","profile":"other"}]}""";

    private static string V3ManifestWithServiceObject()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":{"@id":"https://example.org/other","profile":"other"}}""";

    private static string V3ManifestWithRendering()
        => """{"id":"https://example.org/m/1","type":"Manifest","rendering":[{"id":"https://example.org/pdf","type":"Text","format":"application/pdf"}]}""";

    private static string V3ManifestWithExistingSearchV2()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":[{"id":"https://search.example.org/search/v2/test/book","type":"SearchService2"}]}""";

    private static string V3ManifestWithBothSearchServices()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":[{"id":"https://search.example.org/search/v2/test/book","type":"SearchService2"},{"id":"https://search.example.org/search/v1/test/book","type":"SearchService1"}]}""";

    private static string V3ManifestWithSearchV2ServiceObject()
        => """{"id":"https://example.org/m/1","type":"Manifest","service":{"id":"https://search.example.org/search/v2/test/book","type":"SearchService2"}}""";

    private static string V3ManifestWithExistingTextRef()
        => """{"id":"https://example.org/m/1","type":"Manifest","rendering":[{"id":"https://search.example.org/text/v1/test/book","type":"Text","format":"text/plain"}]}""";

    private static string V3ManifestWithExistingPdfRef()
        => """{"id":"https://example.org/m/1","type":"Manifest","rendering":[{"id":"https://search.example.org/pdf/v1/test/book","type":"Text","format":"application/pdf"}]}""";

    private static string V3ManifestWithExistingPdfAndTextRefs()
        => """{"id":"https://example.org/m/1","type":"Manifest","rendering":[{"id":"https://search.example.org/pdf/v1/test/book","type":"Text","format":"application/pdf"},{"id":"https://search.example.org/text/v1/test/book","type":"Text","format":"text/plain"}]}""";

    private static string V3ManifestWithExistingAnnotationsRef()
        => """{"id":"https://example.org/m/1","type":"Manifest","annotations":[{"id":"https://search.example.org/annotations/manifest/v1/test/book","type":"AnnotationPage"}]}""";

    private static string V3ManifestWithExistingFiguresRef()
        => """{"id":"https://example.org/m/1","type":"Manifest","annotations":[{"id":"https://search.example.org/identified/figures/test/book","type":"AnnotationPage"}]}""";

    private static string V3ManifestWithSearchContexts()
        => """{"id":"https://example.org/m/1","type":"Manifest","@context":["http://iiif.io/api/presentation/3/context.json","http://iiif.io/api/search/2/context.json","http://iiif.io/api/search/1/context.json"]}""";

    private sealed class StubTextStore(
        string? manifestJson,
        string? annotationsJson = null,
        string? figuresJson = null) : ITextStore
    {
        public Task<string?> LoadManifest(string key) => Task.FromResult(manifestJson);
        public Task SaveManifest(string key, string json) => Task.CompletedTask;
        public Task SaveText(string key, Text text) => Task.CompletedTask;
        public Task<Text?> LoadText(string key) => Task.FromResult<Text?>(null);
        public Task SaveAutoComplete(string key, AutoComplete ac) => Task.CompletedTask;
        public Task<AutoComplete?> LoadAutoComplete(string key) => Task.FromResult<AutoComplete?>(null);
        public Task SaveRawText(string key, string raw) => Task.CompletedTask;
        public Task<string?> LoadRawText(string key) => Task.FromResult<string?>(null);
        public Task SavePdf(string key, Stream s) => Task.CompletedTask;
        public Task<Stream?> LoadPdf(string key) => Task.FromResult<Stream?>(null);
        public Task SaveFigures(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadFigures(string key) => Task.FromResult(figuresJson);
        public Task SaveAnnotations(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadAnnotations(string key) => Task.FromResult(annotationsJson);
        public Task<bool> Exists(string key) => Task.FromResult(false);
        public Task SaveCapabilities(string key, int services) => Task.CompletedTask;
        public Task<int?> LoadCapabilities(string key) => Task.FromResult<int?>(null);
        public Task SavePageSequence(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadPageSequence(string key) => Task.FromResult<string?>(null);
        public Task DeleteArtefacts(string key) => Task.CompletedTask;
    }

    private sealed class StubTextCache(Text? text) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult(text);
        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);
        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult<JobServices?>(null);
        public void Invalidate(string key) { }
    }
}
