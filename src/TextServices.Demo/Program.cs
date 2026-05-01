using TextServices.Demo;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
builder.Services.AddSingleton(options);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Inject server-side config into the browser so JS never hard-codes URLs.
app.MapGet("/demo-config", (DemoOptions opts, IWebHostEnvironment env) =>
{
    // Compute file:// URIs for the E2E fixture directories so sourcedata.js
    // doesn't need to hard-code the checkout path.
    var fixturesDir = Path.GetFullPath(
        Path.Combine(env.ContentRootPath, "..", "TextServices.Tests.E2E", "Fixtures", "b2888193x"));

    string? fixtureAlto   = null;
    string? fixtureImages = null;

    if (Directory.Exists(fixturesDir))
    {
        var baseUri   = new Uri(fixturesDir).AbsoluteUri;
        fixtureAlto   = baseUri + "/alto";
        fixtureImages = baseUri + "/images";
    }

    return Results.Ok(new
    {
        builderApi    = opts.BuilderApiBaseUrl.TrimEnd('/'),
        searchApi     = opts.SearchApiBaseUrl.TrimEnd('/'),
        hangfireUrl   = opts.BuilderApiBaseUrl.TrimEnd('/') + "/hangfire",
        fixtureAlto,
        fixtureImages,
    });
});

// Clean URLs — redirect to the static HTML files.
app.MapGet("/",       () => Results.Redirect("/index.html"));
app.MapGet("/builder",() => Results.Redirect("/builder.html"));
app.MapGet("/viewer", () => Results.Redirect("/viewer.html"));
app.MapGet("/compare",     () => Results.Redirect("/compare.html"));
app.MapGet("/annotations", () => Results.Redirect("/annotations.html"));

app.Run();
