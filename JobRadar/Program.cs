using JobRadar.Components;
using JobRadar.Data;
using JobRadar.Services;
using JobRadar.Services.Crawling;
using JobRadar.Services.Providers;
using JobRadar.Services.Storage;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// ── Storage: swap via Storage:Provider in appsettings.json ──────────────────
var storageProvider = builder.Configuration["Storage:Provider"] ?? "sqlite";

if (storageProvider.Equals("json", StringComparison.OrdinalIgnoreCase))
{
    // Flat-file JSON store — zero dependencies, good for dev/demo
    builder.Services.AddSingleton<IJobRadarStore, JsonFileJobRadarStore>();
}
else
{
    // SQLite via EF Core — default, production-ready
    builder.Services.AddDbContext<JobRadarDbContext>(opt =>
        opt.UseSqlite("Data Source=jobradar.db"));
    builder.Services.AddScoped<IJobRadarStore, SqliteJobRadarStore>();
}

builder.Services.AddHttpClient("Crawler", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (compatible; JobRadarBot/1.0)");
});
builder.Services.AddHttpClient();

// ── Auto-discover all [PlaceReader] and [JobReader] providers ────────────────
// Adding a new provider = create the class + add the attribute. No changes here.
builder.Services.AddDiscoveredProviders();

// ── Orchestrator & supporting services ──────────────────────────────────────
builder.Services.AddScoped<ProviderStatusService>();
builder.Services.AddScoped<ProviderOrchestrator>();
builder.Services.AddScoped<CareersCrawlerService>();
builder.Services.AddScoped<SearchCacheService>();
builder.Services.AddScoped<AtsApiClient>();
builder.Services.AddScoped<CareerPageCrawler>();
builder.Services.AddScoped<CompanyCrawlerService>();
// Singleton: one Chromium process shared across all requests
builder.Services.AddSingleton<PlaywrightCrawler>();
builder.Services.AddSingleton<JobSearchState>();

var app = builder.Build();

// Run EF Core migrations only when using SQLite
if (storageProvider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JobRadarDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Skip HTTPS redirect in dev — localhost is already a secure context for geolocation
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Dispose the singleton Playwright browser on shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    var pw = app.Services.GetService<PlaywrightCrawler>();
    pw?.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

app.Run();
