using Shouldly;
using TextServices.Core.Models;
using TextServices.Search.Api.Features.Pdf;
using TextServices.Search.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.SearchApi;

public class PdfHandlerTests
{
    private const string Id = "test/book";

    // -------------------------------------------------------------------------
    // Handle(PdfTriggerRequest) — result codes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Trigger_ServiceDisabled_ReturnsNotFound()
    {
        var handler = MakeTriggerHandler(pdfEnabled: false, pdfExists: false, textExists: false);

        var result = await handler.Handle(new PdfTriggerRequest(Id), CancellationToken.None);

        result.ShouldBe(PdfTriggerResult.NotFound);
    }

    [Fact]
    public async Task Trigger_NoTextArtefact_ReturnsNotFound()
    {
        var handler = MakeTriggerHandler(pdfEnabled: true, pdfExists: false, textExists: false);

        var result = await handler.Handle(new PdfTriggerRequest(Id), CancellationToken.None);

        result.ShouldBe(PdfTriggerResult.NotFound);
    }

    [Fact]
    public async Task Trigger_PdfAlreadyExists_ReturnsAlreadyExists()
    {
        var handler = MakeTriggerHandler(pdfEnabled: true, pdfExists: true, textExists: true);

        var result = await handler.Handle(new PdfTriggerRequest(Id), CancellationToken.None);

        result.ShouldBe(PdfTriggerResult.AlreadyExists);
    }

    [Fact]
    public async Task Trigger_GenerationInProgress_ReturnsQueued()
    {
        var handler = MakeTriggerHandler(
            pdfEnabled: true, pdfExists: false, textExists: true, isGenerating: true);

        var result = await handler.Handle(new PdfTriggerRequest(Id), CancellationToken.None);

        result.ShouldBe(PdfTriggerResult.Queued);
    }

    [Fact]
    public async Task Trigger_EnqueueSucceeds_ReturnsQueued()
    {
        var handler = MakeTriggerHandler(
            pdfEnabled: true, pdfExists: false, textExists: true,
            isGenerating: false, enqueueResult: true);

        var result = await handler.Handle(new PdfTriggerRequest(Id), CancellationToken.None);

        result.ShouldBe(PdfTriggerResult.Queued);
    }

    [Fact]
    public async Task Trigger_QueueFull_ReturnsServiceBusy()
    {
        var handler = MakeTriggerHandler(
            pdfEnabled: true, pdfExists: false, textExists: true,
            isGenerating: false, enqueueResult: false);

        var result = await handler.Handle(new PdfTriggerRequest(Id), CancellationToken.None);

        result.ShouldBe(PdfTriggerResult.ServiceBusy);
    }

    // -------------------------------------------------------------------------
    // Handle(PdfRequest) — basic coverage
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_ServiceDisabled_ReturnsNull()
    {
        var handler = MakeGetHandler(pdfEnabled: false, existingPdf: null, textExists: false);

        var result = await handler.Handle(new PdfRequest(Id), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Get_NoTextArtefact_ReturnsNull()
    {
        var handler = MakeGetHandler(pdfEnabled: true, existingPdf: null, textExists: false);

        var result = await handler.Handle(new PdfRequest(Id), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Get_PdfAlreadyExists_ReturnsCachedStream()
    {
        var stream = new MemoryStream([1, 2, 3]);
        var handler = MakeGetHandler(pdfEnabled: true, existingPdf: stream, textExists: true);

        var result = await handler.Handle(new PdfRequest(Id), CancellationToken.None);

        result.ShouldBeSameAs(stream);
    }

    [Fact]
    public async Task Get_PdfAbsent_CallsEnsureGeneratedThenLoadsStream()
    {
        var generated = new MemoryStream([4, 5, 6]);
        var store = new StubPdfStore(initialPdf: null, textExists: true, afterGeneration: generated);
        var generationService = new StubPdfGenerationService(isGenerating: false, onEnsure: store.MarkGenerated);
        var handler = MakeHandlerWith(store, generationService, new StubPdfGenerationQueue(enqueueResult: true));

        var result = await handler.Handle(new PdfRequest(Id), CancellationToken.None);

        generationService.WasEnsureCalled.ShouldBeTrue();
        result.ShouldBeSameAs(generated);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PdfHandler MakeTriggerHandler(
        bool pdfEnabled,
        bool pdfExists,
        bool textExists,
        bool isGenerating = false,
        bool enqueueResult = true)
    {
        var store = new StubPdfStore(
            initialPdf: pdfExists ? new MemoryStream() : null,
            textExists: textExists);
        var cache = new StubPdfTextCache(pdfEnabled ? null : JobServices.Search);
        var genService = new StubPdfGenerationService(isGenerating);
        var queue = new StubPdfGenerationQueue(enqueueResult);
        return new PdfHandler(store, cache, genService, queue);
    }

    private static PdfHandler MakeGetHandler(bool pdfEnabled, Stream? existingPdf, bool textExists)
    {
        var store = new StubPdfStore(initialPdf: existingPdf, textExists: textExists);
        var cache = new StubPdfTextCache(pdfEnabled ? null : JobServices.Search);
        var genService = new StubPdfGenerationService(isGenerating: false);
        var queue = new StubPdfGenerationQueue(enqueueResult: true);
        return new PdfHandler(store, cache, genService, queue);
    }

    private static PdfHandler MakeHandlerWith(
        ITextStore store,
        IPdfGenerationService generationService,
        IPdfGenerationQueue queue)
        => new(store, new StubPdfTextCache(null), generationService, queue);

    private sealed class StubPdfStore(Stream? initialPdf, bool textExists, Stream? afterGeneration = null)
        : ITextStore
    {
        private bool _generated;

        public void MarkGenerated() => _generated = true;

        public Task<Stream?> LoadPdf(string key)
            => Task.FromResult(_generated ? afterGeneration : initialPdf);

        public Task<bool> Exists(string key) => Task.FromResult(textExists);

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
        public Task SaveCapabilities(string key, int services) => Task.CompletedTask;
        public Task<int?> LoadCapabilities(string key) => Task.FromResult<int?>(null);
        public Task SavePageSequence(string key, string json) => Task.CompletedTask;
        public Task<string?> LoadPageSequence(string key) => Task.FromResult<string?>(null);
        public Task DeleteArtefacts(string key) => Task.CompletedTask;
    }

    /// <summary>
    /// Passes null capabilities (all services enabled) when <paramref name="capabilities"/> is null.
    /// Pass a specific flags value to simulate a capabilities file that excludes certain services.
    /// </summary>
    private sealed class StubPdfTextCache(JobServices? capabilities) : ITextCache
    {
        public Task<Text?> GetTextAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Text?>(null);
        public Task<AutoComplete?> GetAutoCompleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult<AutoComplete?>(null);
        public Task<JobServices?> GetCapabilitiesAsync(string key, CancellationToken ct = default)
            => Task.FromResult(capabilities);
        public void Invalidate(string key) { }
    }

    private sealed class StubPdfGenerationService(bool isGenerating, Action? onEnsure = null)
        : IPdfGenerationService
    {
        public bool WasEnsureCalled { get; private set; }

        public Task EnsureGenerated(string id, CancellationToken ct)
        {
            WasEnsureCalled = true;
            onEnsure?.Invoke();
            return Task.CompletedTask;
        }

        public bool IsGenerating(string id) => isGenerating;
    }

    private sealed class StubPdfGenerationQueue(bool enqueueResult) : IPdfGenerationQueue
    {
        public bool TryEnqueue(string id) => enqueueResult;
        public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => AsyncEnumerable.Empty<string>();
    }
}
