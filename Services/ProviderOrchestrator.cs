using System.Reflection;
using JobRadar.Models;
using JobRadar.Services.Crawling;
using JobRadar.Services.Providers;

namespace JobRadar.Services;

/// <summary>
/// Fans out to all registered/enabled providers in parallel and merges results.
/// Integrates cache layer: serves cached results first, crawls stale companies on-demand.
/// </summary>
public class ProviderOrchestrator(
    IEnumerable<ICompanyProvider> companyProviders,
    IEnumerable<IJobProvider> jobProviders,
    SearchCacheService cache,
    CompanyCrawlerService crawler,
    IConfiguration config,
    ILogger<ProviderOrchestrator> logger)
{
    public async Task StreamCompaniesAsync(
        double lat, double lng,
        Func<List<Company>, string, Task> onBatch,
        int radiusMeters = 0)
    {
        if (radiusMeters <= 0)
            radiusMeters = config.GetValue<int>("Search:RadiusMeters", 20000);

        // ── 1. Serve from cache if fresh ──────────────────────────────────────
        var cached = await cache.GetCachedSearchAsync(lat, lng, radiusMeters);
        if (cached != null)
        {
            logger.LogInformation("Serving {Count} companies from cache (refreshed {Age:hh\\:mm} ago)",
                cached.Companies.Count, DateTime.UtcNow - cached.LastRefreshedAt);
            var cachedCompanies = cached.Companies.Select(ToCompany).ToList();
            if (cachedCompanies.Count > 0)
                await onBatch(cachedCompanies, $"Cache:{cached.LastRefreshedAt:O}");
            return;
        }

        // ── 2. Fresh search: create cache entry, fan out providers ─────────────
        var locationSearch = await cache.CreateOrUpdateSearchAsync(lat, lng, radiusMeters);

        var enabled = companyProviders
            .Where(p => p.IsAvailable && IsProviderEnabled("Companies", p.GetType()))
            .ToList();

        logger.LogInformation("Streaming companies from {Count} provider(s): {Names}",
            enabled.Count, string.Join(", ", enabled.Select(p => p.Name)));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tasks = enabled.Select(async p =>
        {
            try
            {
                var results = await p.GetCompaniesAsync(lat, lng, radiusMeters);

                var fresh = results.Where(r => seen.Add(r.Name)).ToList();
                if (fresh.Count == 0) return;

                // Persist to cache
                var cachedList = new List<CachedCompany>();
                foreach (var r in fresh)
                {
                    var cc = await cache.UpsertCompanyAsync(locationSearch.Id, new CachedCompany
                    {
                        Name = r.Name, Website = r.Website, Industry = r.Industry,
                        Latitude = r.Latitude, Longitude = r.Longitude,
                        DistanceMiles = r.DistanceMiles, SourceProvider = r.SourceProvider,
                    });
                    cachedList.Add(cc);
                }

                await onBatch(cachedList.Select(ToCompany).ToList(), p.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Company provider {Name} failed", p.Name);
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Streams jobs as each provider finishes. Fires career-page crawl for each
    /// discovered company in parallel with the regular job providers.
    /// </summary>
    public async Task StreamJobsAsync(
        double lat, double lng,
        Func<List<Job>, string, Task> onBatch,
        int radiusMiles = 25,
        string? keywords = null)
    {
        var enabled = jobProviders
            .Where(p => p.IsAvailable && IsProviderEnabled("Jobs", p.GetType()))
            .ToList();

        logger.LogInformation("Streaming jobs from {Count} provider(s): {Names}",
            enabled.Count, string.Join(", ", enabled.Select(p => p.Name)));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── API job providers ──────────────────────────────────────────────────
        var apiTasks = enabled.Select(async p =>
        {
            try
            {
                var results = await p.GetJobsAsync(lat, lng, radiusMiles, keywords);
                var fresh = results
                    .Where(r => seen.Add($"{r.Title}|{r.CompanyName}"))
                    .Select(r => new Job
                    {
                        Title = r.Title, Location = r.Location, Description = r.Description,
                        ApplyUrl = r.ApplyUrl, ExperienceLevel = r.ExperienceLevel,
                        IsRemote = r.IsRemote, Skills = r.Skills,
                        IsLocalPosition = JobLocationFilter.IsRelevant(r.Location, r.IsRemote),
                        DateFound = DateTime.UtcNow,
                        Company = new Company { Name = r.CompanyName },
                    }).ToList();
                if (fresh.Count > 0) await onBatch(fresh, p.Name);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Job provider {Name} failed", p.Name); }
        });

        // ── Career-page crawl for cached companies ─────────────────────────────
        var crawlTask = CrawlCachedCompaniesAsync(lat, lng, 20000, onBatch, seen);

        await Task.WhenAll(apiTasks.Append(crawlTask));
    }

    /// <summary>
    /// Crawl career pages for all companies in the cache that need (re)crawling.
    /// Results stream back via onBatch immediately as each company finishes.
    /// </summary>
    private async Task CrawlCachedCompaniesAsync(
        double lat, double lng, int radiusMeters,
        Func<List<Job>, string, Task> onBatch,
        HashSet<string> seen)
    {
        try
        {
            var locationSearch = await cache.GetCachedSearchAsync(lat, lng, radiusMeters);
            if (locationSearch == null) return;

            var toCrawl = locationSearch.Companies
                .Where(c => cache.NeedsCrawl(c) && !string.IsNullOrWhiteSpace(c.Website))
                .ToList();

            logger.LogInformation("Crawling career pages for {Count} companies", toCrawl.Count);

            // Crawl up to 10 companies concurrently to avoid hammering networks
            var semaphore = new SemaphoreSlim(10);
            var crawlTasks = toCrawl.Select(async company =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var cachedJobs = await crawler.CrawlCompanyAsync(company);
                    if (cachedJobs.Count == 0) return;

                    var jobs = cachedJobs
                        .Where(j => seen.Add($"{j.Title}|{company.Name}"))
                        .Select(j => new Job
                        {
                            Title = j.Title, Location = j.Location ?? company.Name,
                            Description = j.Description, ApplyUrl = j.ApplyUrl,
                            ExperienceLevel = j.ExperienceLevel, IsRemote = j.IsRemote,
                            IsLocalPosition = JobLocationFilter.IsRelevant(j.Location, j.IsRemote),
                            Skills = j.Skills, DateFound = DateTime.UtcNow,
                            Company = new Company
                            {
                                Name = company.Name, Website = company.Website,
                                CareersUrl = company.CareersUrl,
                            },
                        }).ToList();

                    if (jobs.Count > 0) await onBatch(jobs, $"Crawled: {company.Name}");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Career crawl failed for {Company}", company.Name);
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(crawlTasks);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Career-page crawl batch failed");
        }
    }

    private bool IsProviderEnabled(string category, Type providerType)
    {
        // Prefer the [PlaceReader]/[JobReader] ConfigKey for stable config paths
        var configKey = providerType.GetCustomAttribute<PlaceReaderAttribute>()?.ConfigKey
                     ?? providerType.GetCustomAttribute<JobReaderAttribute>()?.ConfigKey
                     ?? providerType.Name;

        var key = $"Providers:{category}:{configKey}:Enabled";
        var val = config[key];
        return val == null || bool.TrueString.Equals(val, StringComparison.OrdinalIgnoreCase);
    }

    private static Company ToCompany(CachedCompany c) => new()
    {
        Name           = c.Name,
        Website        = c.Website,
        CareersUrl     = c.CareersUrl,
        Industry       = c.Industry,
        Latitude       = c.Latitude,
        Longitude      = c.Longitude,
        DistanceMiles  = c.DistanceMiles,
        SourceProvider = c.SourceProvider,
    };
}
