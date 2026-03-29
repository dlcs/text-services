using System.ComponentModel.DataAnnotations;
using Hangfire;
using Hangfire.PostgreSql;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Features.Jobs;
using TextServices.Storage;

var builder = WebApplication.CreateBuilder(args);

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

// GET /textbuilder/{**id}
app.MapGet("/textbuilder/{**id}", async (string id, ISender sender) =>
{
    var response = await sender.Send(new GetJobRequest(id));
    return response == null ? Results.NotFound() : Results.Ok(response);
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
