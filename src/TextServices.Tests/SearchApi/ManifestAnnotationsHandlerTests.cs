using System.Text.Json.Nodes;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Search.Api.Features.Annotations;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

public class ManifestAnnotationsHandlerTests
{
    private const string SelfUrl = "https://search.example.org/annotations/manifest/v1/test/book";

    [Fact]
    public async Task Handle_NotFound_ReturnsNull()
    {
        var handler = new ManifestAnnotationsHandler(new StubAnnotationsStore(null), new StubTextCache());

        var result = await handler.Handle(
            new ManifestAnnotationsRequest("missing/book", SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_PatchesSelfUrl()
    {
        var json    = StoredPage("anno/0", "Hello world", "https://example.org/canvas/1#xywh=0,0,100,20");
        var handler = new ManifestAnnotationsHandler(new StubAnnotationsStore(json), new StubTextCache());

        var result = await handler.Handle(
            new ManifestAnnotationsRequest("test/book", SelfUrl), CancellationToken.None);

        result.ShouldNotBeNull();
        result!["id"]!.GetValue<string>().ShouldBe(SelfUrl);
    }

    [Fact]
    public async Task Handle_PrefixesAnnotationIds()
    {
        var json    = StoredPage("anno/0", "Hello world", "https://example.org/canvas/1#xywh=0,0,100,20");
        var handler = new ManifestAnnotationsHandler(new StubAnnotationsStore(json), new StubTextCache());

        var result = await handler.Handle(
            new ManifestAnnotationsRequest("test/book", SelfUrl), CancellationToken.None);

        var items = result!["items"].ShouldBeOfType<JsonArray>();
        items[0]!["id"]!.GetValue<string>().ShouldBe($"{SelfUrl}/anno/0");
    }

    [Fact]
    public async Task Handle_PreservesBodyAndTarget()
    {
        const string target = "https://example.org/canvas/1#xywh=10,20,300,50";
        var json    = StoredPage("anno/0", "Some line text", target);
        var handler = new ManifestAnnotationsHandler(new StubAnnotationsStore(json), new StubTextCache());

        var result = await handler.Handle(
            new ManifestAnnotationsRequest("test/book", SelfUrl), CancellationToken.None);

        var anno = result!["items"]![0]!;
        anno["motivation"]!.GetValue<string>().ShouldBe("supplementing");
        anno["body"]!["value"]!.GetValue<string>().ShouldBe("Some line text");
        anno["target"]!.GetValue<string>().ShouldBe(target);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string StoredPage(string annoId, string bodyValue, string target) =>
        $$"""
        {
          "id": "",
          "type": "AnnotationPage",
          "textGranularity": "line",
          "items": [
            {
              "id": "{{annoId}}",
              "type": "Annotation",
              "motivation": "supplementing",
              "body": { "type": "TextualBody", "value": "{{bodyValue}}", "format": "text/plain" },
              "target": "{{target}}"
            }
          ]
        }
        """;

    private sealed class StubAnnotationsStore(string? annotationsJson) : ITextStore
    {
        public Task<string?> LoadAnnotations(string key) => Task.FromResult(annotationsJson);
        public Task SaveAnnotations(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadManifest(string key) => Task.FromResult<string?>(null);
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
        public Task<string?> LoadFigures(string key) => Task.FromResult<string?>(null);
        public Task<bool> Exists(string key) => Task.FromResult(false);
        public Task SaveCapabilities(string key, int services) => Task.CompletedTask;
        public Task<int?> LoadCapabilities(string key) => Task.FromResult<int?>(null);
        public Task SavePageSequence(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadPageSequence(string key) => Task.FromResult<string?>(null);
        public Task DeleteArtefacts(string key) => Task.CompletedTask;
    }

    private sealed class StubTextCache : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Text?>(null);
        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);
        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult<JobServices?>(null);
        public void Invalidate(string key) { }
    }
}
