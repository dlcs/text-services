using System.Net.Http.Headers;
using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;
using TextServices.Builder.Api.Services.Notifications;
using TextServices.Infrastructure.Http;
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

builder.Services.AddHangfireServices(builder.Configuration);

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

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();

builder.Services.AddScoped<IResourceFetcher>(sp => new ResourceFetcher(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetService<IAmazonS3>()));

// ---- Manifest services ------------------------------------------------------

builder.Services.AddSingleton<IManifestReducer, ManifestReducer>()
    .AddSingleton<IManifestSynthesiser, ManifestSynthesiser>()
    .AddScoped<IManifestFetcher, ManifestFetcher>()
    .AddScoped<IAltoFetcher, AltoFetcher>()
    .AddScoped<IVttFetcher, VttFetcher>()
    .AddScoped<IAnnotationPageFetcher, AnnotationPageFetcher>();

// ---- Notifications ----------------------------------------------------------

builder.Services.AddAWSService<IAmazonSimpleNotificationService>();
builder.Services.AddSingleton<IJobNotifier>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TextServicesOptions>>();
    if (string.IsNullOrEmpty(opts.Value.Notifications.TopicArn)) return new NullJobNotifier();
    return ActivatorUtilities.CreateInstance<SnsJobNotifier>(sp);
});

// ---- Storage ----------------------------------------------------------------

builder.Services.AddSingleton<ITextStore>(sp =>
{
    var storage = sp.GetRequiredService<IOptions<TextServicesOptions>>().Value.Storage;
    if (!string.IsNullOrEmpty(storage.S3?.BucketName))
    {
        return new S3TextStore(
            new S3TextStoreOptions { BucketName = storage.S3.BucketName, KeyPrefix = storage.S3.KeyPrefix },
            sp.GetRequiredService<IAmazonS3>());
    }

    return new FileSystemTextStore(new FileSystemTextStoreOptions { RootPath = storage.RootPath });
});

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
