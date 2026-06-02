using System.IO.Compression;
using System.Text.Json.Serialization;
using Amazon.S3;
using AsyncKeyedLock;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using TextServices.Infrastructure.Http;
using TextServices.Pdf;
using TextServices.Search.Api.Configuration;
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

builder.Services.Configure<SearchApiOptions>(builder.Configuration.GetSection("TextServices"));
builder.Services.ConfigureForwardedHeaders(builder.Configuration);

// ---- Storage ----------------------------------------------------------------

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

if (!string.IsNullOrEmpty(builder.Configuration["TextServices:Storage:S3:BucketName"]))
{
    Log.Debug("Using S3 storage for text artefacts");
    builder.Services.AddAWSService<IAmazonS3>();
}

builder.Services.AddSingleton<ITextStore>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<SearchApiOptions>>().Value;
    if (!string.IsNullOrEmpty(opts.Storage.S3.BucketName))
    {
        var s3Opts = Options.Create(new S3TextStoreOptions
        {
            BucketName = opts.Storage.S3.BucketName,
            KeyPrefix = opts.Storage.S3.KeyPrefix
        });
        return ActivatorUtilities.CreateInstance<S3TextStore>(sp, s3Opts);
    }
    return ActivatorUtilities.CreateInstance<FileSystemTextStore>(
        sp,
        new FileSystemTextStoreOptions { RootPath = opts.Storage.FileSystem.RootPath });
});

// ITextStore is also injected directly into TextAugmentedHandler (manifest is plain JSON,
// not routed through the Text/AutoComplete cache).

// ---- PDF --------------------------------------------------------------------

builder.Services.AddPdfServices();

// ---- Cache ------------------------------------------------------------------

builder.Services.AddMemoryCache(opts =>
    opts.SizeLimit = builder.Configuration.GetSection("TextServices").GetValue<int?>("CacheMaxEntries") ?? 20);
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
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseForwardedHeaders();
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
