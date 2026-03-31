using AsyncKeyedLock;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using TextServices.Search.Api.Configuration;
using TextServices.Pdf;
using TextServices.Search.Api.Features.Autocomplete;
using TextServices.Search.Api.Features.Figures;
using TextServices.Search.Api.Features.Pdf;
using TextServices.Search.Api.Features.PlainText;
using TextServices.Search.Api.Features.Search;
using TextServices.Search.Api.Features.TextAugmented;
using TextServices.Search.Api.Services;
using TextServices.Storage;

var builder = WebApplication.CreateBuilder(args);

// ---- CORS -------------------------------------------------------------------

var corsOrigins = builder.Configuration.GetSection("CorsAllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0)
        p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader();
}));

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
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(60));

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
app.MapGet("/pdf/v1/{**id}", async (
    string id,
    ISender sender) =>
{
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
