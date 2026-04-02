using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using Hangfire;
using Hangfire.PostgreSql;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Builder.Api.Services;
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

var tsOptions = builder.Configuration
    .GetSection("TextServices")
    .Get<TextServicesOptions>() ?? new TextServicesOptions();

builder.Services.AddSingleton(tsOptions);

// ---- EF Core / PostgreSQL ---------------------------------------------------

var connectionString = builder.Configuration.GetConnectionString("BuilderDb")
    ?? throw new InvalidOperationException("ConnectionStrings:BuilderDb is required.");

builder.Services.AddDbContext<BuilderDbContext>(o =>
    o.UseNpgsql(connectionString));

// ---- Hangfire ---------------------------------------------------------------

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));

builder.Services.AddHangfireServer();

// ---- MediatR ----------------------------------------------------------------

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ---- Manifest services ------------------------------------------------------

builder.Services.AddSingleton<IManifestReducer, ManifestReducer>();
builder.Services.AddScoped<IManifestFetcher, ManifestFetcher>();
builder.Services.AddScoped<IAltoFetcher, AltoFetcher>();
builder.Services.AddScoped<IVttFetcher, VttFetcher>();

builder.Services.AddHttpClient("Manifest", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/ld+json", 0.9));
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("Alto", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0");
    // Accept anything — IIIF implementations vary widely in the Content-Type
    // they set on ALTO files (application/xml, text/xml, text/plain,
    // application/octet-stream, or nothing). We parse whatever comes back as XML.
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("*/*"));
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("Vtt", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TextServices/1.0 (+https://github.com/tomcrane/TextServices)");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ---- Storage ----------------------------------------------------------------

builder.Services.AddSingleton<ITextStore>(_ =>
    new FileSystemTextStore(new FileSystemTextStoreOptions
    {
        RootPath = tsOptions.Storage.RootPath
    }));

// ---- HTTP -------------------------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHangfireDashboard("/hangfire");
}

app.UseCors();
app.UseHttpsRedirection();

// ---- Endpoints --------------------------------------------------------------

// POST /textbuilder
app.MapPost("/textbuilder", async (JobInstruction instruction, ISender sender) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(instruction,
            new ValidationContext(instruction), validationResults, validateAllProperties: true))
    {
        var errors = validationResults
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.ErrorMessage ?? "Invalid").ToArray());
        return Results.ValidationProblem(errors);
    }

    var result = await sender.Send(new CreateJobRequest(instruction));

    if (result.AlreadyExists)
        return Results.Conflict(result.Response);

    return Results.Accepted($"/textbuilder/{instruction.Id}", result.Response);
});

// GET /textbuilder  — paged list of all jobs, newest first
app.MapGet("/textbuilder", async (ISender sender, int page = 1, int pageSize = 20, string? status = null) =>
{
    var result = await sender.Send(new ListJobsRequest(page, pageSize, status));
    return Results.Ok(result);
});

// GET /textbuilder/{**id}
app.MapGet("/textbuilder/{**id}", async (string id, ISender sender) =>
{
    var response = await sender.Send(new GetJobRequest(id));
    return response == null ? Results.NotFound() : Results.Ok(response);
});

// PUT /textbuilder/{**id}  — reprocess an existing job
app.MapPut("/textbuilder/{**id}", async (string id, ISender sender) =>
{
    var result = await sender.Send(new ReprocessJobRequest(id));
    return result.Status switch
    {
        ReprocessStatus.NotFound => Results.NotFound(),
        ReprocessStatus.Conflict => Results.Conflict(result.Response),
        _ => Results.Accepted($"/textbuilder/{id}", result.Response),
    };
});

// DELETE /textbuilder/{**id}
app.MapDelete("/textbuilder/{**id}", async (string id, ISender sender) =>
{
    var found = await sender.Send(new DeleteJobRequest(id));
    return found ? Results.NoContent() : Results.NotFound();
});

app.Run();

// Make Program visible to integration test projects
public partial class Program { }
