using Microsoft.Extensions.Options;
using TextServices.Demo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Inject server-side config into the browser so JS never hard-codes URLs.
app.MapGet("/demo-config", (IOptions<DemoOptions> opts, IWebHostEnvironment env) =>
{
    // Compute file:// URIs for the E2E fixture directories so sourcedata.js
    // doesn't need to hard-code the checkout path.
    var fixturesDir = Path.GetFullPath(
        Path.Combine(env.ContentRootPath, "..", "TextServices.Tests.E2E", "Fixtures", "b2888193x"));

    string? fixtureAlto = null;
    string? fixtureImages = null;

    if (Directory.Exists(fixturesDir))
    {
        var baseUri = new Uri(fixturesDir).AbsoluteUri;
        fixtureAlto = baseUri + "/alto";
        fixtureImages = baseUri + "/images";
    }

    return Results.Ok(new
    {
        builderApi = opts.Value.BuilderApiBaseUrl.TrimEnd('/'),
        searchApi = opts.Value.SearchApiBaseUrl.TrimEnd('/'),
        hangfireUrl = opts.Value.BuilderApiBaseUrl.TrimEnd('/') + "/hangfire",
        fixtureAlto,
        fixtureImages,
    });
});

// Clean URLs — redirect to the static HTML files.
app.MapGet("/", () => Results.Redirect("/index.html"));
app.MapGet("/builder", () => Results.Redirect("/builder.html"));
app.MapGet("/viewer", () => Results.Redirect("/viewer.html"));
app.MapGet("/compare", () => Results.Redirect("/compare.html"));
app.MapGet("/annotations", () => Results.Redirect("/annotations.html"));

app.Run();
