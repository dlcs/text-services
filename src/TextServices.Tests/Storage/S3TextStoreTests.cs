using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TextServices.Core.Models;
using TextServices.Core.Providers;
using TextServices.Storage;

namespace TextServices.Tests.Storage;

/// <summary>
/// Tests for <see cref="S3TextStore"/> using an in-memory <see cref="IAmazonS3"/> stub.
/// No real AWS credentials or network calls are required.
/// </summary>
public class S3TextStoreTests
{
    private const string Bucket = "test-bucket";

    // -------------------------------------------------------------------------
    // Text
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAndLoad_Text_RoundTrips()
    {
        var fake = new FakeS3();
        var store = MakeStore(fake);
        var text = BuildText("hello world");

        await store.SaveText("a/b", text);
        var loaded = await store.LoadText("a/b");

        loaded.ShouldNotBeNull();
        loaded!.NormalisedFullText.ShouldBe(text.NormalisedFullText);
    }

    [Fact]
    public async Task LoadText_Missing_ReturnsNull()
    {
        var store = MakeStore(new FakeS3());
        var result = await store.LoadText("no/such/key");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SaveText_UsesCorrectS3Key()
    {
        var fake = new FakeS3();
        var store = MakeStore(fake, prefix: "ts/");

        await store.SaveText("2/books/my-book", BuildText("x"));

        fake.Keys.ShouldContain("ts/2/books/my-book/text.bin");
    }

    // -------------------------------------------------------------------------
    // AutoComplete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAndLoad_AutoComplete_RoundTrips()
    {
        var fake = new FakeS3();
        var store = MakeStore(fake);
        var ac = new AutoComplete
        {
            Buckets = new Dictionary<string, HashSet<string>>
            {
                ["hel"] = ["hello", "helium"]
            }
        };

        await store.SaveAutoComplete("a/b", ac);
        var loaded = await store.LoadAutoComplete("a/b");

        loaded.ShouldNotBeNull();
        loaded!.Buckets["hel"].ShouldContain("hello");
    }

    [Fact]
    public async Task LoadAutoComplete_Missing_ReturnsNull()
    {
        var store = MakeStore(new FakeS3());
        var result = await store.LoadAutoComplete("no/such/key");
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Manifest
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAndLoad_Manifest_RoundTrips()
    {
        var fake = new FakeS3();
        var store = MakeStore(fake);

        await store.SaveManifest("a/b", """{"id":"https://example.org/m/1"}""");
        var loaded = await store.LoadManifest("a/b");

        loaded.ShouldBe("""{"id":"https://example.org/m/1"}""");
    }

    [Fact]
    public async Task LoadManifest_Missing_ReturnsNull()
    {
        var store = MakeStore(new FakeS3());
        var result = await store.LoadManifest("no/such/key");
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Exists
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exists_AfterSaveText_ReturnsTrue()
    {
        var fake = new FakeS3();
        var store = MakeStore(fake);

        await store.SaveText("a/b", BuildText("x"));
        var exists = await store.Exists("a/b");

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task Exists_NoText_ReturnsFalse()
    {
        var store = MakeStore(new FakeS3());
        var exists = await store.Exists("no/such/key");
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task Exists_AutoCompleteOnly_ReturnsFalse()
    {
        // Exists checks text.bin only — autocomplete alone is not sufficient.
        var fake = new FakeS3();
        var store = MakeStore(fake);

        await store.SaveAutoComplete("a/b", new AutoComplete());
        var exists = await store.Exists("a/b");

        exists.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Key prefix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task KeyPrefix_TrailingSlashNormalised()
    {
        // Whether prefix is supplied with or without trailing slash, keys are the same.
        var fakeWith = new FakeS3();
        var fakeWithout = new FakeS3();

        await MakeStore(fakeWith, prefix: "prefix/").SaveText("k", BuildText("x"));
        await MakeStore(fakeWithout, prefix: "prefix").SaveText("k", BuildText("x"));

        fakeWith.Keys.First().ShouldBe(fakeWithout.Keys.First());
    }

    [Fact]
    public async Task NoKeyPrefix_KeyStartsWithJobId()
    {
        var fake = new FakeS3();
        var store = MakeStore(fake, prefix: "");

        await store.SaveText("my/book", BuildText("x"));

        fake.Keys.First().ShouldStartWith("my/book/");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static S3TextStore MakeStore(FakeS3 fake, string prefix = "")
        => new(Options.Create(new S3TextStoreOptions { BucketName = Bucket, KeyPrefix = prefix }), fake,
            NullLogger<S3TextStore>.Instance);

    private static Text BuildText(string words)
    {
        var builder = new TextBuilder();
        // Minimal single-page text with no ALTO — just exercise the protobuf round-trip.
        return builder.Build().Text;
    }

    // ---- Minimal in-memory IAmazonS3 stub -----------------------------------

    private sealed class FakeS3 : AmazonS3Client
    {
        private readonly Dictionary<string, byte[]> _objects = new();

        public IReadOnlyCollection<string> Keys => _objects.Keys;

        // Use a no-credential config that never makes real network calls.
        public FakeS3() : base(
            new Amazon.Runtime.BasicAWSCredentials("fake", "fake"),
            new AmazonS3Config { ServiceURL = "http://localhost:0" })
        { }

        public override Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            request.InputStream!.CopyTo(ms);
            _objects[request.Key] = ms.ToArray();
            return Task.FromResult(new PutObjectResponse());
        }

        public override Task<GetObjectResponse> GetObjectAsync(
            string bucketName, string key,
            CancellationToken ct = default)
        {
            if (!_objects.TryGetValue(key, out var bytes))
                throw new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound };

            var response = new GetObjectResponse
            {
                ResponseStream = new MemoryStream(bytes)
            };
            return Task.FromResult(response);
        }

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(
            string bucketName, string key,
            CancellationToken ct = default)
        {
            if (!_objects.ContainsKey(key))
                throw new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound };

            return Task.FromResult(new GetObjectMetadataResponse());
        }
    }
}
