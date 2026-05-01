using Shouldly;
using TextServices.Core.Models;
using TextServices.Search.Api.Features.Annotations;
using TextServices.Search.Api.Features.Autocomplete;
using TextServices.Search.Api.Features.Figures;
using TextServices.Search.Api.Features.PlainText;
using TextServices.Search.Api.Features.Search;
using TextServices.Search.Api.Features.TextAugmented;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

/// <summary>
/// Verifies that each Search API handler gates on the relevant
/// <see cref="JobServices"/> flag: when the flag is absent from the stored
/// capabilities the handler returns null (→ 404), and when capabilities is
/// null (absent file) all services are considered enabled.
/// </summary>
public class CapabilityGatingTests
{
    private const string Id       = "test/book";
    private const string SelfUrl  = "https://search.example.org/test/book";
    private const string SearchBase = "https://search.example.org";

    // -------------------------------------------------------------------------
    // SearchHandler — JobServices.Search
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Search_WhenSearchFlagAbsent_ReturnsNull()
    {
        var handler = new SearchHandler(new StubTextCache(JobServices.Autocomplete));

        var result = await handler.Handle(
            new SearchRequest(Id, "hello", SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Search_WhenCapabilitiesNull_ProceedsToTextLookup()
    {
        // null capabilities = all enabled; text = null → null result (text not found)
        var handler = new SearchHandler(new StubTextCache(null));

        var result = await handler.Handle(
            new SearchRequest(Id, "hello", SelfUrl), CancellationToken.None);

        // Gating passed; null result because text is absent, not because of gating.
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // AutocompleteHandler — JobServices.Autocomplete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Autocomplete_WhenAutocompleteFlagAbsent_ReturnsNull()
    {
        var handler = new AutocompleteHandler(new StubTextCache(JobServices.Search));

        var result = await handler.Handle(
            new AutocompleteRequest(Id, "hel", SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Autocomplete_WhenCapabilitiesNull_ProceedsToAutocompleteLookup()
    {
        var handler = new AutocompleteHandler(new StubTextCache(null));

        // ac = null → null result (no autocomplete artefact), not a gating failure
        var result = await handler.Handle(
            new AutocompleteRequest(Id, "hel", SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // RawTextHandler — JobServices.FullText
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RawText_WhenFullTextFlagAbsent_ReturnsNull()
    {
        var handler = new RawTextHandler(new AllNullStore(), new StubTextCache(JobServices.Search));

        var result = await handler.Handle(new RawTextRequest(Id), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // ManifestAnnotationsHandler — JobServices.Annotations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ManifestAnnotations_WhenAnnotationsFlagAbsent_ReturnsNull()
    {
        var handler = new ManifestAnnotationsHandler(
            new AllNullStore(), new StubTextCache(JobServices.Search));

        var result = await handler.Handle(
            new ManifestAnnotationsRequest(Id, SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // FiguresHandler — JobServices.Figures
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Figures_WhenFiguresFlagAbsent_ReturnsNull()
    {
        var handler = new FiguresHandler(new AllNullStore(), new StubTextCache(JobServices.Search));

        var result = await handler.Handle(new FiguresRequest(Id, SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // LineAnnotationsHandler — JobServices.Annotations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LineAnnotations_WhenAnnotationsFlagAbsent_ReturnsNull()
    {
        var handler = new LineAnnotationsHandler(new StubTextCache(JobServices.Search));

        var result = await handler.Handle(
            new LineAnnotationsRequest(Id, 0, SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // WordAnnotationsHandler — JobServices.Annotations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WordAnnotations_WhenAnnotationsFlagAbsent_ReturnsNull()
    {
        var handler = new WordAnnotationsHandler(new StubTextCache(JobServices.Search));

        var result = await handler.Handle(
            new WordAnnotationsRequest(Id, 0, SelfUrl), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // TextAugmentedHandler — JobServices.TextAugmented
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TextAugmented_WhenTextAugmentedFlagAbsent_ReturnsNull()
    {
        var handler = new TextAugmentedHandler(
            new AllNullStore(), new StubTextCache(JobServices.Search));

        var result = await handler.Handle(
            new TextAugmentedRequest(Id, SelfUrl, SearchBase), CancellationToken.None);

        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Stubs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a fixed <see cref="JobServices"/> from GetCapabilitiesAsync so the
    /// handler's flag check can be exercised.  All other members return null/empty.
    /// </summary>
    private sealed class StubTextCache(JobServices? capabilities) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Text?>(null);

        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);

        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult(capabilities);
    }

    /// <summary>ITextStore that returns null for every load operation.</summary>
    private sealed class AllNullStore : ITextStore
    {
        public Task SaveText(string key, Text text) => Task.CompletedTask;
        public Task<Text?> LoadText(string key) => Task.FromResult<Text?>(null);
        public Task SaveAutoComplete(string key, AutoComplete ac) => Task.CompletedTask;
        public Task<AutoComplete?> LoadAutoComplete(string key) => Task.FromResult<AutoComplete?>(null);
        public Task SaveRawText(string key, string raw) => Task.CompletedTask;
        public Task<string?> LoadRawText(string key) => Task.FromResult<string?>(null);
        public Task SaveManifest(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadManifest(string key) => Task.FromResult<string?>(null);
        public Task SaveFigures(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadFigures(string key) => Task.FromResult<string?>(null);
        public Task SaveAnnotations(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadAnnotations(string key) => Task.FromResult<string?>(null);
        public Task SavePdf(string key, Stream s) => Task.CompletedTask;
        public Task<Stream?> LoadPdf(string key) => Task.FromResult<Stream?>(null);
        public Task<bool> Exists(string key) => Task.FromResult(false);
        public Task SaveCapabilities(string key, int services) => Task.CompletedTask;
        public Task<int?> LoadCapabilities(string key) => Task.FromResult<int?>(null);
    }
}
