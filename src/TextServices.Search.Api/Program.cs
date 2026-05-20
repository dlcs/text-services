using System.IO.Compression;
using AsyncKeyedLock;
using Microsoft.AspNetCore.ResponseCompression;
using Serilog;
using Serilog.Events;
using TextServices.Search.Api.Configuration;
using TextServices.Infrastructure.Http;
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

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
Log.Information("Application starting...");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((hostContext, loggerConfig) =>
    loggerConfig
        .ReadFrom.Configuration(hostContext.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithCorrelationId());

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

builder.Services
    .AddHttpContextAccessor()
    .AddCorrelationIdHeaderPropagation()
    .AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(opts =>
    opts.GetLevel = (ctx, _, _) =>
        ctx.Request.Path.StartsWithSegments("/health")
            ? LogEventLevel.Verbose
            : LogEventLevel.Information);
app.UseResponseCompression();
app.UseCors();
app.UseHttpsRedirection();

// ---- Endpoints --------------------------------------------------------------

app.MapCacheEndpoints()
   .MapSearchEndpoints()
   .MapAutocompleteEndpoints()
   .MapPlainTextEndpoints()
   .MapPdfEndpoints()
   .MapFiguresEndpoints()
   .MapAnnotationEndpoints()
   .MapTextAugmentedEndpoints()
   .MapProxyEndpoints();

app.Run();

// Make Program visible to integration test projects
public partial class Program { }
