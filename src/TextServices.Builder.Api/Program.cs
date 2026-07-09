using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Infrastructure.Http;

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

builder.Services.AddAwsServices(builder.Configuration)
    .AddFetchingServices()
    .AddNotificationServices()
    .AddTextStorage(builder.Configuration);

// ---- HTTP -------------------------------------------------------------------

builder.Services
    .AddHttpContextAccessor()
    .AddCorrelationIdHeaderPropagation();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
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
