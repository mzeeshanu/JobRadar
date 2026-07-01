using JobRadar.Models;

namespace JobRadar.Services.Storage;

/// <summary>
/// Persistence contract for the Job Radar cache layer.
/// Swap implementations via Storage:Provider in appsettings.json:
///   "sqlite"  — EF Core + SQLite (default, production-ready)
///   "json"    — single JSON file on disk (zero-config, good for dev/demo)
/// </summary>
public interface IJobRadarStore
{
    // ── Location searches ─────────────────────────────────────────────────────
    Task<LocationSearch?> FindSearchAsync(double lat, double lng, int radiusMeters);
    Task<LocationSearch> SaveSearchAsync(LocationSearch search);

    // ── Companies ─────────────────────────────────────────────────────────────
    Task<CachedCompany?> FindCompanyAsync(int locationSearchId, string name);
    Task<CachedCompany> SaveCompanyAsync(CachedCompany company);

    // ── Jobs ──────────────────────────────────────────────────────────────────
    Task ReplaceJobsAsync(int cachedCompanyId, List<CachedJob> jobs);
    Task PurgeExpiredJobsAsync();

    // ── Stats ─────────────────────────────────────────────────────────────────
    Task<StoreStats> GetStatsAsync();
}

public record StoreStats(int Searches, int Companies, int LiveJobs, int ExpiredJobs);
