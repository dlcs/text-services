using System.Text.Json;
using System.Xml.Linq;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Jobs;
using TextServices.Builder.Api.Services;
using TextServices.Storage;

namespace TextServices.Tests.BuilderApi;

/// <summary>
/// Integration tests for <see cref="TextBuildJob"/> using an in-memory EF Core
/// context and filesystem storage in a temp directory.
/// </summary>
public sealed class TextBuildJobTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BuilderDbContext _db;
    private readonly ITextStore _textStore;

    public TextBuildJobTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TBJTests_{Guid.NewGuid():N}");

        var options = new DbContextOptionsBuilder<BuilderDbContext>()
            .UseInMemoryDatabase($"TextBuildJob_{Guid.NewGuid():N}")
            .Options;
        _db = new BuilderDbContext(options);

        _textStore = new FileSystemTextStore(
            new FileSystemTextStoreOptions { RootPath = _tempDir });
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Happy path — inline sourceData
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InlineSourceData_CompletesAndSavesArtefacts()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        var job = await CreateJob("test/inline",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalPages.ShouldBe(1);
        updated.PagesCompleted.ShouldBe(1);
        updated.TotalWordCount.ShouldBe(2);   // "hello", "world"
        updated.Errors.ShouldBeNull();

        (await _textStore.Exists(job.Id)).ShouldBeTrue();
        (await _textStore.LoadAutoComplete(job.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_InlineSourceData_MultiPage_AccumulatesAllWords()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
            new() { Id = "https://example.org/c/2", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/2.xml" },
        };

        var job = await CreateJob("test/multipage",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("the quick"),
            ["https://example.org/alto/2.xml"] = SampleAlto("brown fox"),
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalWordCount.ShouldBe(4);
        updated.TotalImageCount.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // Happy path — sourceUri (manifest fetch)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SourceUri_FetchesManifestAndSavesJson()
    {
        var job = await CreateJob("test/uri",
            sourceUri: "https://example.org/manifest");

        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        var manifestFetcher = new FakeManifestFetcher(
            "https://example.org/manifest",
            """{"@context":"https://iiif.io/api/presentation/3/context.json"}""",
            pages);

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("parliament"),
        });

        var sut = MakeJob(manifestFetcher: manifestFetcher, altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalPages.ShouldBe(1);
        updated.SourceDataJson.ShouldNotBeNull();

        // Manifest JSON should have been stored.
        var storedManifest = await _textStore.LoadManifest(job.Id);
        storedManifest.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // Sparse / missing ALTO
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SparseCanvas_NoTextUri_SkippedSilently()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500, TextUri = null },
        };

        var job = await CreateJob("test/sparse",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var sut = MakeJob();
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.PagesCompleted.ShouldBe(1);
        updated.TotalWordCount.ShouldBe(0);
        updated.Errors.ShouldBeNull();

        // No artefacts saved for an all-sparse manifest.
        (await _textStore.Exists(job.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AltoReturnsNull_PageSkippedSilently()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/missing.xml" },
        };

        var job = await CreateJob("test/null-alto",
            sourceDataJson: JsonSerializer.Serialize(pages));

        // Fetcher returns null (simulates 404).
        var altoFetcher = new FakeAltoFetcher(_ => Task.FromResult<XElement?>(null));

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalWordCount.ShouldBe(0);
        updated.Errors.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Error handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AltoFetchThrows_ErrorAccumulatedJobStillCompletes()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/bad.xml" },
            new() { Id = "https://example.org/c/2", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/2.xml" },
        };

        var job = await CreateJob("test/partial-error",
            sourceDataJson: JsonSerializer.Serialize(pages));

        int call = 0;
        var altoFetcher = new FakeAltoFetcher(_ =>
        {
            call++;
            if (call == 1) throw new HttpRequestException("Connection refused");
            return Task.FromResult<XElement?>(SampleAlto("good content"));
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);     // still completes
        updated.TotalWordCount.ShouldBe(2);                // page 2 was processed
        updated.Errors.ShouldNotBeNull();                   // page 1 error recorded
        updated.Errors.ShouldContain("https://example.org/c/1");
    }

    [Fact]
    public async Task ExecuteAsync_ManifestFetchThrows_JobSetToFailed()
    {
        var job = await CreateJob("test/bad-manifest",
            sourceUri: "https://example.org/bad-manifest");

        var manifestFetcher = new FakeManifestFetcher(
            _ => throw new HttpRequestException("Not found"));

        var sut = MakeJob(manifestFetcher: manifestFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Failed);
        updated.Errors.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // VTT transcript pages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_VttCanvas_FetchesVttAndSavesTemporalIndex()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/audio", Width = 0, Height = 0,
                    TextUri = "https://example.org/transcript.vtt", Format = "text/vtt" },
        };

        var job = await CreateJob("test/vtt-canvas",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var vttFetcher = new FakeVttFetcher(_ => Task.FromResult<string?>(SimpleVtt()));

        var sut = MakeJob(vttFetcher: vttFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalWordCount.ShouldBeGreaterThan(0);
        updated.Errors.ShouldBeNull();

        (await _textStore.Exists(job.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_VttFetchReturnsNull_PageSkippedSilently()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/audio", Width = 0, Height = 0,
                    TextUri = "https://example.org/transcript.vtt", Format = "text/vtt" },
        };

        var job = await CreateJob("test/vtt-null",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var vttFetcher = new FakeVttFetcher(_ => Task.FromResult<string?>(null));

        var sut = MakeJob(vttFetcher: vttFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalWordCount.ShouldBe(0);
        updated.Errors.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MixedAltoAndVttPages_BothProcessed()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
            new() { Id = "https://example.org/c/audio", Width = 0, Height = 0,
                    TextUri = "https://example.org/transcript.vtt", Format = "text/vtt" },
        };

        var job = await CreateJob("test/mixed-alto-vtt",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        var vttFetcher = new FakeVttFetcher(_ => Task.FromResult<string?>(SimpleVtt()));

        var sut = MakeJob(altoFetcher: altoFetcher, vttFetcher: vttFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var updated = await _db.Jobs.FindAsync(job.Id);
        updated!.Status.ShouldBe(JobStatus.Completed);
        updated.TotalWordCount.ShouldBeGreaterThan(0);
        updated.TotalImageCount.ShouldBe(2);
        updated.Errors.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Synthetic manifest (sourceData path)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InlineSourceData_SyntheticManifestSaved()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        var job = await CreateJob("test/sd-manifest",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        // Use the real synthesiser so the manifest contains the canvas ids.
        var sut = MakeJob(manifestSynthesiser: new ManifestSynthesiser(new TextServices.Builder.Api.Configuration.TextServicesOptions()), altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var storedManifest = await _textStore.LoadManifest(job.Id);
        storedManifest.ShouldNotBeNull();
        storedManifest.ShouldContain("Manifest");
        storedManifest.ShouldContain("https://example.org/c/1");
    }

    // -------------------------------------------------------------------------
    // Service-flag gating — what gets saved when Services is restricted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SearchOnly_SavesTextButNotAutocomplete()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        var job = await CreateJob("test/search-only",
            sourceDataJson: JsonSerializer.Serialize(pages),
            services: JobServices.Search);

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        (await _textStore.LoadText(job.Id)).ShouldNotBeNull();
        (await _textStore.LoadAutoComplete(job.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AutocompleteOnly_SavesAutocompleteButNotText()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        var job = await CreateJob("test/ac-only",
            sourceDataJson: JsonSerializer.Serialize(pages),
            services: JobServices.Autocomplete);

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        (await _textStore.LoadText(job.Id)).ShouldBeNull();
        (await _textStore.LoadAutoComplete(job.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_RestrictedServices_SavesCapabilitiesFile()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        var job = await CreateJob("test/caps-saved",
            sourceDataJson: JsonSerializer.Serialize(pages),
            services: JobServices.Search | JobServices.Autocomplete);

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        var caps = await _textStore.LoadCapabilities(job.Id);
        caps.ShouldNotBeNull();
        var saved = (JobServices)caps.Value;
        saved.HasFlag(JobServices.Search).ShouldBeTrue();
        saved.HasFlag(JobServices.Autocomplete).ShouldBeTrue();
        saved.HasFlag(JobServices.Pdf).ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AllServices_DoesNotSaveCapabilitiesFile()
    {
        var pages = new List<PageInstruction>
        {
            new() { Id = "https://example.org/c/1", Width = 1000, Height = 1500,
                    TextUri = "https://example.org/alto/1.xml" },
        };

        // Default services = All
        var job = await CreateJob("test/caps-not-saved",
            sourceDataJson: JsonSerializer.Serialize(pages));

        var altoFetcher = FakeAlto(new Dictionary<string, XElement>
        {
            ["https://example.org/alto/1.xml"] = SampleAlto("hello world"),
        });

        var sut = MakeJob(altoFetcher: altoFetcher);
        await sut.ExecuteAsync(job.Id, FakeCancellationToken.Instance);

        // No capabilities file means "all enabled" — backward-compatible default.
        (await _textStore.LoadCapabilities(job.Id)).ShouldBeNull();
    }

    private static string SimpleVtt() =>
        """
        WEBVTT

        00:00:05.000 --> 00:00:08.500
        Hello world

        00:00:10.000 --> 00:00:15.000
        foo bar

        """;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<BuilderJob> CreateJob(string id,
        string? sourceUri = null, string? sourceDataJson = null,
        JobServices services = JobServices.All)
    {
        var job = new BuilderJob
        {
            Id = id,
            SourceUri = sourceUri,
            SourceDataJson = sourceDataJson,
            Services = (int)services,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private TextBuildJob MakeJob(
        IManifestFetcher? manifestFetcher = null,
        IManifestSynthesiser? manifestSynthesiser = null,
        IAltoFetcher? altoFetcher = null,
        IVttFetcher? vttFetcher = null,
        IAnnotationPageFetcher? annotationPageFetcher = null)
    {
        return new TextBuildJob(
            _db,
            manifestFetcher ?? new FakeManifestFetcher(_ => throw new InvalidOperationException("Unexpected manifest fetch")),
            manifestSynthesiser ?? new FakeManifestSynthesiser(),
            altoFetcher ?? new FakeAltoFetcher(_ => Task.FromResult<XElement?>(null)),
            vttFetcher ?? new FakeVttFetcher(_ => Task.FromResult<string?>(null)),
            annotationPageFetcher ?? new FakeAnnotationPageFetcher(_ => Task.FromResult<string?>(null)),
            _textStore,
            new TextServicesOptions(),
            NullLogger<TextBuildJob>.Instance);
    }

    private sealed class FakeManifestSynthesiser : IManifestSynthesiser
    {
        public string Synthesise(IReadOnlyList<PageInstruction> pages) =>
            """{"type":"Manifest","id":""}""";
    }

    private static IAltoFetcher FakeAlto(Dictionary<string, XElement> map) =>
        new FakeAltoFetcher(uri =>
            Task.FromResult(map.TryGetValue(uri, out var xml) ? xml : null));

    /// <summary>
    /// Generates a minimal valid ALTO v3 XML element containing a single
    /// TextLine with the given space-separated words.
    /// </summary>
    private static XElement SampleAlto(string words)
    {
        XNamespace ns = "http://www.loc.gov/standards/alto/ns-v3#";
        int x = 0;
        var strings = words.Split(' ').Select(w =>
        {
            var el = new XElement(ns + "String",
                new XAttribute("CONTENT", w),
                new XAttribute("HPOS", x),
                new XAttribute("VPOS", 10),
                new XAttribute("WIDTH", 50),
                new XAttribute("HEIGHT", 20));
            x += 60;
            return el;
        }).ToArray<object>();

        return new XElement(ns + "alto",
            new XElement(ns + "Layout",
                new XElement(ns + "Page",
                    new XAttribute("WIDTH", 1000),
                    new XAttribute("HEIGHT", 1500),
                    new XElement(ns + "PrintSpace",
                        new XElement(ns + "TextBlock",
                            new XElement(ns + "TextLine",
                                strings))))));
    }

    // ---- Fakes ---------------------------------------------------------------

    private sealed class FakeAltoFetcher(Func<string, Task<XElement?>> impl) : IAltoFetcher
    {
        public Task<XElement?> FetchAsync(string uri, CancellationToken ct = default) => impl(uri);
    }

    private sealed class FakeVttFetcher(Func<string, Task<string?>> impl) : IVttFetcher
    {
        public Task<string?> FetchAsync(string uri, CancellationToken ct = default) => impl(uri);
    }

    private sealed class FakeAnnotationPageFetcher(Func<string, Task<string?>> impl) : IAnnotationPageFetcher
    {
        public Task<string?> FetchAsync(string uri, CancellationToken ct = default) => impl(uri);
    }

    private sealed class FakeManifestFetcher : IManifestFetcher
    {
        private readonly Func<string, Task<ManifestFetchResult>> _impl;

        public FakeManifestFetcher(string uri, string json, IReadOnlyList<PageInstruction> pages)
            : this(_ => Task.FromResult(new ManifestFetchResult(json, pages))) { }

        public FakeManifestFetcher(Func<string, Task<ManifestFetchResult>> impl) => _impl = impl;

        public Task<ManifestFetchResult> FetchAndReduce(string uri, CancellationToken ct = default)
            => _impl(uri);
    }

    private sealed class FakeCancellationToken : IJobCancellationToken
    {
        public static readonly FakeCancellationToken Instance = new();
        public CancellationToken ShutdownToken => CancellationToken.None;
        public void ThrowIfCancellationRequested() { }
    }
}
