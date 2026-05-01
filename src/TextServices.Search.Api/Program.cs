using System.IO.Compression;
using AsyncKeyedLock;
using MediatR;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using TextServices.Search.Api.Configuration;
using TextServices.Pdf;
using TextServices.Search.Api.Features.Annotations;
using TextServices.Search.Api.Features.Autocomplete;
using TextServices.Search.Api.Features.Figures;
using TextServices.Search.Api.Features.Pdf;
using TextServices.Search.Api.Features.PlainText;
using TextServices.Search.Api.Features.Search;
using TextServices.Search.Api.Features.TextAugmented;
using TextServices.Search.Api.Services;
using TextServices.Storage;

var builder = WebApplication.CreateBuilder(args);

// ---- Response compression ---------------------------------------------------

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/json",
        "application/ld+json",
    ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o =>
    o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o =>
    o.Level = CompressionLevel.Fastest);

// ---- CORS -------------------------------------------------------------------
// All Search API endpoints are public read-only IIIF services; the IIIF spec
// requires Access-Control-Allow-Origin: * on all responses.

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ---- Configuration ----------------------------------------------------------

var options = builder.Configuration
    .GetSection("TextServices")
    .Get<SearchApiOptions>() ?? new SearchApiOptions();

builder.Services.AddSingleton(options);

// ---- Storage ----------------------------------------------------------------

builder.Services.AddSingleton<ITextStore>(_ =>
    new FileSystemTextStore(new FileSystemTextStoreOptions
    {
        RootPath = options.StorageRootPath
    }));

// ITextStore is also injected directly into TextAugmentedHandler (manifest is plain JSON,
// not routed through the Text/AutoComplete cache).

// ---- PDF --------------------------------------------------------------------

builder.Services.AddSingleton<PdfBuilder>();
builder.Services.AddHttpClient(PdfBuilder.HttpClientName)
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(60);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0 (+https://github.com/tomcrane/TextServices)");
    });

// ---- Cache ------------------------------------------------------------------

builder.Services.AddMemoryCache(opts => opts.SizeLimit = options.CacheMaxEntries);
builder.Services.AddSingleton(new AsyncKeyedLocker<string>());
builder.Services.AddSingleton<ITextCache, TextCache>();

// ---- MediatR ----------------------------------------------------------------

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ---- HTTP -------------------------------------------------------------------

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseResponseCompression();
app.UseCors();
app.UseHttpsRedirection();

// ---- Endpoints --------------------------------------------------------------

// GET /search/v2/{**id}?q={term}
app.MapGet("/search/v2/{**id}", async (
    string id, string? q,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"search/v2/{id}", q);

    var result = await sender.Send(new SearchV2Request(id, q ?? string.Empty, selfUrl));
    if (result == null) return Results.NotFound();

    result.Ignored = GetIgnoredParams(ctx);
    return Results.Json(result);
});

// GET /autocomplete/v2/{**id}?q={term}
app.MapGet("/autocomplete/v2/{**id}", async (
    string id, string? q,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"autocomplete/v2/{id}", q);

    var result = await sender.Send(new AutocompleteV2Request(id, q ?? string.Empty, selfUrl));
    if (result == null) return Results.NotFound();

    return Results.Json(result);
});

// GET /search/v1/{**id}?q={term}
app.MapGet("/search/v1/{**id}", async (
    string id, string? q,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"search/v1/{id}", q);

    var result = await sender.Send(new SearchRequest(id, q ?? string.Empty, selfUrl));
    if (result == null) return Results.NotFound();

    result.Ignored = GetIgnoredParams(ctx);
    return Results.Json(result);
});

// GET /autocomplete/v1/{**id}?q={term}
app.MapGet("/autocomplete/v1/{**id}", async (
    string id, string? q,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"autocomplete/v1/{id}", q);

    var result = await sender.Send(new AutocompleteRequest(id, q ?? string.Empty, selfUrl));
    if (result == null) return Results.NotFound();

    return Results.Json(result);
});

// GET /text/v1/{**id}
app.MapGet("/text/v1/{**id}", async (
    string id,
    ISender sender) =>
{
    var result = await sender.Send(new RawTextRequest(id));
    if (result == null) return Results.NotFound();
    return Results.Text(result, "text/plain");
});

// GET /pdf/v1/{**id}  — synchronous; generates on first request, then serves from storage
// Accepts optional .pdf suffix (e.g. /pdf/v1/my/book.pdf) for nicer save-as filenames.
app.MapGet("/pdf/v1/{**id}", async (
    string id,
    ISender sender) =>
{
    id = StripPdfExtension(id);
    var stream = await sender.Send(new PdfRequest(id));
    if (stream == null) return Results.NotFound();
    return Results.Stream(stream, "application/pdf",
        enableRangeProcessing: false);
});

// POST /pdf/v1/{**id}  — async trigger for M2M / bulk pre-generation
app.MapPost("/pdf/v1/{**id}", async (
    string id,
    ISender sender,
    HttpContext ctx) =>
{
    id = StripPdfExtension(id);
    var started = await sender.Send(new PdfTriggerRequest(id));
    if (!started)
    {
        // PDF already exists — redirect the caller to download it
        var location = BuildSelfUrl(options, ctx, $"pdf/v1/{id}", null);
        return Results.Ok(new { location });
    }
    var locationUrl = BuildSelfUrl(options, ctx, $"pdf/v1/{id}", null);
    return Results.Accepted(locationUrl);
});

// GET /identified/figures/{**id}
app.MapGet("/identified/figures/{**id}", async (
    string id,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"identified/figures/{id}", null);
    var result  = await sender.Send(new FiguresRequest(id, selfUrl));
    if (result == null) return Results.NotFound();

    return Results.Json(result);
});

// GET /annotations/manifest/v1/{**id}  — manifest-level line annotations (stored at build time)
app.MapGet("/annotations/manifest/v1/{**id}", async (
    string id,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"annotations/manifest/v1/{id}", null);
    var result  = await sender.Send(new ManifestAnnotationsRequest(id, selfUrl));
    if (result == null) return Results.NotFound();
    return Results.Json(result);
});

// GET /annotations/lines/v1/{n}/{**id}  — line-level annotation page for canvas n
app.MapGet("/annotations/lines/v1/{n:int}/{**id}", async (
    int n, string id,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"annotations/lines/v1/{n}/{id}", null);
    var result  = await sender.Send(new LineAnnotationsRequest(id, n, selfUrl));
    if (result == null) return Results.NotFound();
    return Results.Json(result);
});

// GET /annotations/words/v1/{n}/{**id}  — word-level annotation page for canvas n
app.MapGet("/annotations/words/v1/{n:int}/{**id}", async (
    int n, string id,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl = BuildSelfUrl(options, ctx, $"annotations/words/v1/{n}/{id}", null);
    var result  = await sender.Send(new WordAnnotationsRequest(id, n, selfUrl));
    if (result == null) return Results.NotFound();
    return Results.Json(result);
});

// GET /proxy/image?uri={uri}
// Proxies local file:// image URIs so IIIF viewers can load painting annotation bodies
// from synthesised manifests.  For non-proxiable schemes (e.g. s3://) returns a 1×1
// transparent PNG placeholder so the manifest remains structurally valid.
// The Search API hosts this endpoint (not the Builder API) because the Search API is
// always running when a viewer needs to load images from a stored manifest.
app.MapGet("/proxy/image", async (string uri, CancellationToken ct) =>
{
    if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        return Results.BadRequest("Invalid URI.");

    if (parsed.Scheme == "file")
    {
        // Guard: return placeholder unless AllowFileImageProxy is explicitly enabled.
        // This prevents the proxy from exposing access-controlled images even if a
        // proxy URL ends up in a manifest on a deployment where proxying is disabled.
        if (!options.AllowFileImageProxy)
            return Results.Bytes(TextServices.Search.Api.ProxyImagePlaceholder.Png, "image/png");

        var path = parsed.LocalPath;
        if (!File.Exists(path)) return Results.NotFound();

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            ".webp"           => "image/webp",
            _                 => "application/octet-stream",
        };
        return Results.Stream(File.OpenRead(path), contentType);
    }

    if (parsed.Scheme == "s3")
        return Results.Bytes(TextServices.Search.Api.ProxyImagePlaceholder.Png, "image/png");

    return Results.BadRequest($"URI scheme '{parsed.Scheme}' is not supported by this proxy.");
});

// GET /text-augmented/v3/{**id}
app.MapGet("/text-augmented/v3/{**id}", async (
    string id,
    ISender sender,
    HttpContext ctx) =>
{
    var selfUrl    = BuildSelfUrl(options, ctx, $"text-augmented/v3/{id}", null);
    var searchBase = string.IsNullOrEmpty(options.BaseUrl)
        ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
        : options.BaseUrl.TrimEnd('/');

    var result = await sender.Send(new TextAugmentedRequest(id, selfUrl, searchBase));
    if (result == null) return Results.NotFound();

    return Results.Json(result);
});

app.Run();

// ---- Helpers ----------------------------------------------------------------

static string StripPdfExtension(string id) =>
    id.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;

static string BuildSelfUrl(SearchApiOptions opts, HttpContext ctx, string path, string? q)
{
    var baseUrl = string.IsNullOrEmpty(opts.BaseUrl)
        ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
        : opts.BaseUrl.TrimEnd('/');

    var url = $"{baseUrl}/{path}";
    return string.IsNullOrWhiteSpace(q) ? url : $"{url}?q={Uri.EscapeDataString(q)}";
}

static string[]? GetIgnoredParams(HttpContext ctx)
{
    // Parameters defined by the IIIF Search v1 spec that we recognise but don't process.
    string[] knownIgnored = ["motivation", "date", "user", "box"];
    var ignored = ctx.Request.Query.Keys
        .Where(k => knownIgnored.Contains(k, StringComparer.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(ctx.Request.Query[k]))
        .ToArray();
    return ignored.Length > 0 ? ignored : null;
}

// Make Program visible to integration test projects
public partial class Program { }
