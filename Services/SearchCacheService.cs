using JobRadar.Models;
using JobRadar.Services.Storage;

namespace JobRadar.Services;

/// <summary>
/// Business logic layer: TTL checks, staleness rules, crawl scheduling.
/// Persistence is delegated to IJobRadarStore — swap the store without touching this class.
/// </summary>
public class SearchCacheService(IJobRadarStore store, IConfiguration config, ILogger<SearchCacheService> logger)
{
    // ── TTLs — read from config so they can be tuned without recompiling ────
    private TimeSpan LocationSearchTtl =>
        TimeSpan.FromHours(config.GetValue<double>("Cache:CompanyTtlHours", 24));
    private TimeSpan JobsTtl =>
        TimeSpan.FromHours(config.GetValue<double>("Cache:JobTtlHours", 8));
    private TimeSpan FailedCrawlRetry =>
        TimeSpan.FromHours(config.GetValue<double>("Cache:FailedCrawlRetryHours", 1));

    // ── Location search cache ───────────────────────────────────────────────

    public async Task<LocationSearch?> GetCachedSearchAsync(double lat, double lng, int radiusMeters)
    {
        var rLat = Math.Round(lat, 2);
        var rLng = Math.Round(lng, 2);

        var cached = await store.FindSearchAsync(rLat, rLng, radiusMeters);
        if (cached == null) return null;

        var age = DateTime.UtcNow - cached.LastRefreshedAt;
        if (age > LocationSearchTtl)
        {
            logger.LogInformation("Location search cache expired ({Age:hh\\:mm} old) — will refresh", age);
            return null;
        }

        if (cached.Companies.Count == 0)
        {
            logger.LogInformation("Cache entry exists but has no companies — will re-fetch");
            return null;
        }

        logger.LogInformation("Cache hit for ({Lat},{Lng}) — {Count} companies, refreshed {Age:hh\\:mm} ago",
            rLat, rLng, cached.Companies.Count, age);
        return cached;
    }

    public async Task<LocationSearch> CreateOrUpdateSearchAsync(double lat, double lng, int radiusMeters)
    {
        return await store.SaveSearchAsync(new LocationSearch
        {
            Lat             = Math.Round(lat, 2),
            Lng             = Math.Round(lng, 2),
            RadiusMeters    = radiusMeters,
            CreatedAt       = DateTime.UtcNow,
            LastRefreshedAt = DateTime.UtcNow,
        });
    }

    // ── Company cache ───────────────────────────────────────────────────────

    public async Task<CachedCompany> UpsertCompanyAsync(int locationSearchId, CachedCompany incoming)
    {
        var existing = await store.FindCompanyAsync(locationSearchId, incoming.Name);
        if (existing != null)
        {
            // Merge: update metadata, preserve crawl history
            existing.Website        = incoming.Website        ?? existing.Website;
            existing.CareersUrl     = incoming.CareersUrl     ?? existing.CareersUrl;
            existing.Industry       = incoming.Industry       ?? existing.Industry;
            existing.DistanceMiles  = incoming.DistanceMiles;
            existing.SourceProvider = incoming.SourceProvider;
            return await store.SaveCompanyAsync(existing);
        }

        incoming.LocationSearchId = locationSearchId;
        return await store.SaveCompanyAsync(incoming);
    }

    public async Task UpdateCompanyCareersInfoAsync(CachedCompany company)
    {
        await store.SaveCompanyAsync(company);
    }

    // ── Job cache ───────────────────────────────────────────────────────────

    public bool NeedsCrawl(CachedCompany company)
    {
        if (company.CrawlStatus == CrawlStatus.Pending) return true;
        if (company.CrawlStatus == CrawlStatus.Failed)
            return company.LastCrawledAt == null ||
                   DateTime.UtcNow - company.LastCrawledAt > FailedCrawlRetry;
        return company.JobsAreStale(JobsTtl);
    }

    public async Task SaveJobsAsync(CachedCompany company, List<CachedJob> jobs, string sourceProvider)
    {
        var expiry = DateTime.UtcNow.Add(JobsTtl);
        foreach (var job in jobs)
        {
            job.DateFound      = DateTime.UtcNow;
            job.ExpiresAt      = expiry;
            job.SourceProvider = sourceProvider;
        }

        await store.ReplaceJobsAsync(company.Id, jobs);

        company.LastCrawledAt = DateTime.UtcNow;
        company.CrawlStatus   = jobs.Count > 0 ? CrawlStatus.Success : CrawlStatus.Empty;
        company.CrawlError    = null;
        await store.SaveCompanyAsync(company);

        logger.LogInformation("Cached {Count} jobs for {Company} via {Provider}",
            jobs.Count, company.Name, sourceProvider);
    }

    public async Task MarkCrawlFailedAsync(CachedCompany company, string error)
    {
        company.CrawlStatus   = CrawlStatus.Failed;
        company.LastCrawledAt = DateTime.UtcNow;
        company.CrawlError    = error;
        await store.SaveCompanyAsync(company);
    }

    public Task PurgeExpiredJobsAsync() => store.PurgeExpiredJobsAsync();

    public async Task<CacheStats> GetStatsAsync()
    {
        var s = await store.GetStatsAsync();
        return new CacheStats(s.Searches, s.Companies, s.LiveJobs, s.ExpiredJobs);
    }
}

public record CacheStats(int Searches, int Companies, int LiveJobs, int ExpiredJobs);
