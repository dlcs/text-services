using System.Net.Http.Headers;
using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Infrastructure.Http;
using TextServices.Builder.Api.Services;
using TextServices.Storage;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
Log.Information("Application starting...");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((hostContext, loggerConfig) =>
    loggerConfig
        .ReadFrom.Configuration(hostContext.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithCorrelationId());

// ---- CORS -------------------------------------------------------------------

var corsOrigins = builder.Configuration.GetSection("CorsAllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0)
        p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader();
}));

// ---- Configuration ----------------------------------------------------------

builder.Services.Configure<TextServicesOptions>(builder.Configuration.GetSection("TextServices"));

// ---- EF Core / PostgreSQL ---------------------------------------------------

builder.Services.AddDbContext<BuilderDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("BuilderDb")
        ?? throw new InvalidOperationException("ConnectionStrings:BuilderDb is required."))
     .UseSnakeCaseNamingConvention());

// ---- Hangfire ---------------------------------------------------------------

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("BuilderDb")
            ?? throw new InvalidOperationException("ConnectionStrings:BuilderDb is required."))));

builder.Services.AddHangfireServer();

// ---- MediatR ----------------------------------------------------------------

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ---- Fetching ---------------------------------------------------------------

// Single named HttpClient used by ResourceFetcher for http/https URIs.
// Accept prefers JSON (for manifests/annotation pages) and falls back to */*
// (for ALTO XML, VTT, or anything else). Servers return the right content type
// regardless of negotiation in practice, but the preference is a courtesy.
builder.Services.AddHttpClient("Resource", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/ld+json", 0.9));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
    client.Timeout = TimeSpan.FromSeconds(30);
});

// IAmazonS3 is optional — register it here when S3 support is needed.
// ResourceFetcher receives null when it is absent and throws only if an s3:// URI is actually used.
// builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client());

builder.Services.AddScoped<IResourceFetcher>(sp => new ResourceFetcher(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetService<IAmazonS3>()));

// ---- Manifest services ------------------------------------------------------

builder.Services.AddSingleton<IManifestReducer, ManifestReducer>();
builder.Services.AddSingleton<IManifestSynthesiser, ManifestSynthesiser>();
builder.Services.AddScoped<IManifestFetcher, ManifestFetcher>();
builder.Services.AddScoped<IAltoFetcher, AltoFetcher>();
builder.Services.AddScoped<IVttFetcher, VttFetcher>();
builder.Services.AddScoped<IAnnotationPageFetcher, AnnotationPageFetcher>();

// ---- Storage ----------------------------------------------------------------

builder.Services.AddSingleton<ITextStore>(sp =>
    new FileSystemTextStore(new FileSystemTextStoreOptions
    {
        RootPath = sp.GetRequiredService<IOptions<TextServicesOptions>>().Value.Storage.RootPath
    }));

// ---- HTTP -------------------------------------------------------------------

builder.Services
    .AddHttpContextAccessor()
    .AddCorrelationIdHeaderPropagation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BuilderDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHangfireDashboard("/hangfire");
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(opts =>
    opts.GetLevel = (ctx, _, _) =>
        ctx.Request.Path.StartsWithSegments("/health")
            ? LogEventLevel.Verbose
            : LogEventLevel.Information);
app.UseCors();
app.UseHttpsRedirection();

// ---- Endpoints --------------------------------------------------------------

app.MapHealthChecks("/health");
app.MapJobEndpoints();

BuilderDbContextConfiguration.TryRunMigrations(app.Configuration, app.Logger);

app.Run();

// Make Program visible to integration test projects
public partial class Program { }
