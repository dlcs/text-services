using Shouldly;
using TextServices.Core.Models;
using TextServices.Search.Api.Features.PlainText;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

public class RawTextHandlerTests
{
    [Fact]
    public async Task Handle_RawTextExists_ReturnsText()
    {
        var handler = new RawTextHandler(new StubTextStore("hello world"));

        var result = await handler.Handle(new RawTextRequest("some/book"), CancellationToken.None);

        result.ShouldBe("hello world");
    }

    [Fact]
    public async Task Handle_NoRawText_ReturnsNull()
    {
        var handler = new RawTextHandler(new StubTextStore(null));

        var result = await handler.Handle(new RawTextRequest("missing/book"), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------

    private sealed class StubTextStore(string? rawText) : ITextStore
    {
        public Task<string?> LoadRawText(string key) => Task.FromResult(rawText);
        public Task SavePdf(string key, Stream s) => Task.CompletedTask;
        public Task<Stream?> LoadPdf(string key) => Task.FromResult<Stream?>(null);
        public Task SaveRawText(string key, string raw) => Task.CompletedTask;
        public Task SaveText(string key, Text text) => Task.CompletedTask;
        public Task<Text?> LoadText(string key) => Task.FromResult<Text?>(null);
        public Task SaveAutoComplete(string key, AutoComplete ac) => Task.CompletedTask;
        public Task<AutoComplete?> LoadAutoComplete(string key) => Task.FromResult<AutoComplete?>(null);
        public Task SaveManifest(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadManifest(string key) => Task.FromResult<string?>(null);
        public Task SaveFigures(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadFigures(string key) => Task.FromResult<string?>(null);
        public Task<bool> Exists(string key) => Task.FromResult(false);
    }
}
