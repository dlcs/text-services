using TextServices.Demo;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
builder.Services.AddSingleton(options);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Inject server-side config into the browser so JS never hard-codes URLs.
app.MapGet("/demo-config", (DemoOptions opts) => Results.Ok(new
{
    builderApi  = opts.BuilderApiBaseUrl.TrimEnd('/'),
    searchApi   = opts.SearchApiBaseUrl.TrimEnd('/'),
    hangfireUrl = opts.BuilderApiBaseUrl.TrimEnd('/') + "/hangfire",
}));

// Clean URLs — redirect to the static HTML files.
app.MapGet("/",       () => Results.Redirect("/index.html"));
app.MapGet("/builder",() => Results.Redirect("/builder.html"));
app.MapGet("/viewer", () => Results.Redirect("/viewer.html"));
app.MapGet("/compare",     () => Results.Redirect("/compare.html"));
app.MapGet("/annotations", () => Results.Redirect("/annotations.html"));

app.Run();
