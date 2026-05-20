using Shouldly;
using TextServices.Core.Models;
using TextServices.Storage;

namespace TextServices.Tests.Storage;

/// <summary>
/// Integration tests for <see cref="FileSystemTextStore"/> using a temporary
/// directory that is cleaned up after each test class run.
/// </summary>
public sealed class FileSystemTextStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemTextStore _store;

    public FileSystemTextStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TextServicesTests_{Guid.NewGuid():N}");
        _store = new FileSystemTextStore(new FileSystemTextStoreOptions { RootPath = _tempDir });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Exists
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exists_BeforeSave_ReturnsFalse()
    {
        var result = await _store.Exists("a/b/c");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task Exists_AfterSaveText_ReturnsTrue()
    {
        var text = MakeText("hello world");
        await _store.SaveText("a/b/c", text);

        var result = await _store.Exists("a/b/c");
        result.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Text round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadText_BeforeSave_ReturnsNull()
    {
        var result = await _store.LoadText("missing/key");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_Text_RoundTrips()
    {
        var text = MakeText("the quick brown fox");
        await _store.SaveText("2/books/my-book", text);

        var loaded = await _store.LoadText("2/books/my-book");

        loaded.ShouldNotBeNull();
        loaded.NormalisedFullText.ShouldBe(text.NormalisedFullText);
        loaded.RawFullText.ShouldBe(text.RawFullText);
        loaded.Words.Count.ShouldBe(text.Words.Count);
        loaded.Images.Length.ShouldBe(text.Images.Length);
    }

    [Fact]
    public async Task SaveText_KeyWithSlashes_CreatesNestedDirectories()
    {
        var text = MakeText("nested path test");
        await _store.SaveText("region/collection/item", text);

        var expectedDir = Path.Combine(_tempDir, "region", "collection", "item");
        Directory.Exists(expectedDir).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveText_FlatKey_CreatesDirectoryUnderRoot()
    {
        var text = MakeText("flat");
        await _store.SaveText("flatkey", text);

        var expectedDir = Path.Combine(_tempDir, "flatkey");
        Directory.Exists(expectedDir).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveText_OverwritesExistingFile()
    {
        var text1 = MakeText("first version");
        var text2 = MakeText("second version");

        await _store.SaveText("overwrite/test", text1);
        await _store.SaveText("overwrite/test", text2);

        var loaded = await _store.LoadText("overwrite/test");
        loaded.ShouldNotBeNull();
        loaded.NormalisedFullText.ShouldBe("second version");
    }

    // -------------------------------------------------------------------------
    // AutoComplete round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadAutoComplete_BeforeSave_ReturnsNull()
    {
        var result = await _store.LoadAutoComplete("missing/key");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_AutoComplete_RoundTrips()
    {
        var ac = new AutoComplete
        {
            Buckets = new Dictionary<string, HashSet<string>>
            {
                ["qui"] = ["quick", "quiet"],
                ["bro"] = ["brown"],
            }
        };

        await _store.SaveAutoComplete("ac/test/key", ac);
        var loaded = await _store.LoadAutoComplete("ac/test/key");

        loaded.ShouldNotBeNull();
        loaded.Buckets.Count.ShouldBe(2);
        loaded.Buckets["qui"].ShouldContain("quick");
        loaded.Buckets["qui"].ShouldContain("quiet");
        loaded.Buckets["bro"].ShouldContain("brown");
    }

    [Fact]
    public async Task SaveAutoComplete_DoesNotAffectExists_WhenNoTextSaved()
    {
        var ac = new AutoComplete { Buckets = new Dictionary<string, HashSet<string>>() };
        await _store.SaveAutoComplete("ac/only/key", ac);

        // Exists checks for the text file, not autocomplete
        var exists = await _store.Exists("ac/only/key");
        exists.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Manifest round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadManifest_BeforeSave_ReturnsNull()
    {
        var result = await _store.LoadManifest("missing/manifest");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_Manifest_RoundTrips()
    {
        const string json = """{"@context":"https://iiif.io/api/presentation/3/context.json","id":"https://example.org/manifest"}""";

        await _store.SaveManifest("manifest/test", json);
        var loaded = await _store.LoadManifest("manifest/test");

        loaded.ShouldBe(json);
    }

    [Fact]
    public async Task SaveManifest_OverwritesExistingFile()
    {
        await _store.SaveManifest("manifest/overwrite", """{"id":"v1"}""");
        await _store.SaveManifest("manifest/overwrite", """{"id":"v2"}""");

        var loaded = await _store.LoadManifest("manifest/overwrite");
        loaded.ShouldBe("""{"id":"v2"}""");
    }

    // -------------------------------------------------------------------------
    // Annotations round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadAnnotations_BeforeSave_ReturnsNull()
    {
        var result = await _store.LoadAnnotations("missing/annotations");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_Annotations_RoundTrips()
    {
        const string json = """{"type":"AnnotationPage","textGranularity":"line","items":[]}""";

        await _store.SaveAnnotations("annotations/test", json);
        var loaded = await _store.LoadAnnotations("annotations/test");

        loaded.ShouldBe(json);
    }

    [Fact]
    public async Task SaveAnnotations_OverwritesExistingFile()
    {
        await _store.SaveAnnotations("annotations/overwrite", """{"id":"v1"}""");
        await _store.SaveAnnotations("annotations/overwrite", """{"id":"v2"}""");

        var loaded = await _store.LoadAnnotations("annotations/overwrite");
        loaded.ShouldBe("""{"id":"v2"}""");
    }

    // -------------------------------------------------------------------------
    // Capabilities round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadCapabilities_BeforeSave_ReturnsNull()
    {
        var result = await _store.LoadCapabilities("missing/key");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAndLoad_Capabilities_RoundTrips()
    {
        const int services = 0b00000011; // Search | Autocomplete

        await _store.SaveCapabilities("caps/test", services);
        var loaded = await _store.LoadCapabilities("caps/test");

        loaded.ShouldBe(services);
    }

    [Fact]
    public async Task SaveAndLoad_Capabilities_AllBitsSet()
    {
        // -1 (all bits) is the default stored value for JobServices.All.
        await _store.SaveCapabilities("caps/all", -1);
        var loaded = await _store.LoadCapabilities("caps/all");

        loaded.ShouldBe(-1);
    }

    [Fact]
    public async Task SaveCapabilities_OverwritesExisting()
    {
        await _store.SaveCapabilities("caps/overwrite", 1);
        await _store.SaveCapabilities("caps/overwrite", 3);

        var loaded = await _store.LoadCapabilities("caps/overwrite");
        loaded.ShouldBe(3);
    }

    // -------------------------------------------------------------------------
    // Key isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DifferentKeys_StoredIndependently()
    {
        var textA = MakeText("content for key a");
        var textB = MakeText("content for key b");

        await _store.SaveText("key/a", textA);
        await _store.SaveText("key/b", textB);

        var loadedA = await _store.LoadText("key/a");
        var loadedB = await _store.LoadText("key/b");

        loadedA.ShouldNotBeNull();
        loadedB.ShouldNotBeNull();
        loadedA.NormalisedFullText.ShouldBe("content for key a");
        loadedB.NormalisedFullText.ShouldBe("content for key b");
    }

    [Fact]
    public async Task TextAndAutoComplete_StoredSeparatelyUnderSameKey()
    {
        var text = MakeText("hello world");
        var ac = new AutoComplete
        {
            Buckets = new Dictionary<string, HashSet<string>> { ["hel"] = ["hello"] }
        };

        await _store.SaveText("shared/key", text);
        await _store.SaveAutoComplete("shared/key", ac);

        var dir = Path.Combine(_tempDir, "shared", "key");
        File.Exists(Path.Combine(dir, "text.bin")).ShouldBeTrue();
        File.Exists(Path.Combine(dir, "autocomplete.bin")).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Text MakeText(string normalisedFullText)
    {
        var words = new Dictionary<int, Word>();
        int pos = 0;
        int wd = 0;
        foreach (var token in normalisedFullText.Split(' '))
        {
            words[pos] = new Word
            {
                ContentNorm = token,
                ContentRaw = token,
                PosNorm = pos,
                PosRaw = pos,
                Wd = wd++,
                Li = wd,
                X = 0,
                Y = 0,
                W = 100,
                H = 20,
            };
            pos += token.Length + 1; // +1 for the space
        }

        return new Text
        {
            NormalisedFullText = normalisedFullText,
            RawFullText = normalisedFullText,
            Words = words,
            Images = [new Image { StartCharacter = 0, ImageIdentifier = "https://example.org/canvas/1" }],
            ComposedBlocks = [],
        };
    }
}
