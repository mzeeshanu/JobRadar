using JobRadar.Data;
using JobRadar.Models;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.Services.Storage;

/// <summary>
/// SQLite-backed store using EF Core. Default production implementation.
/// Configure: Storage:Provider = "sqlite"  (default when key is absent)
/// </summary>
public class SqliteJobRadarStore(JobRadarDbContext db) : IJobRadarStore
{
    public async Task<LocationSearch?> FindSearchAsync(double lat, double lng, int radiusMeters)
    {
        return await db.LocationSearches
            .Include(s => s.Companies)
                .ThenInclude(c => c.Jobs.Where(j => j.ExpiresAt > DateTime.UtcNow))
            .FirstOrDefaultAsync(s =>
                Math.Abs(s.Lat - lat) < 0.01 &&
                Math.Abs(s.Lng - lng) < 0.01 &&
                s.RadiusMeters == radiusMeters);
    }

    public async Task<LocationSearch> SaveSearchAsync(LocationSearch search)
    {
        var existing = await db.LocationSearches.FirstOrDefaultAsync(s =>
            Math.Abs(s.Lat - search.Lat) < 0.01 &&
            Math.Abs(s.Lng - search.Lng) < 0.01 &&
            s.RadiusMeters == search.RadiusMeters);

        if (existing != null)
        {
            existing.LastRefreshedAt = search.LastRefreshedAt;
            await db.SaveChangesAsync();
            return existing;
        }

        db.LocationSearches.Add(search);
        await db.SaveChangesAsync();
        return search;
    }

    public async Task<CachedCompany?> FindCompanyAsync(int locationSearchId, string name)
    {
        return await db.CachedCompanies
            .Include(c => c.Jobs)
            .FirstOrDefaultAsync(c => c.LocationSearchId == locationSearchId && c.Name == name);
    }

    public async Task<CachedCompany> SaveCompanyAsync(CachedCompany company)
    {
        if (company.Id == 0)
            db.CachedCompanies.Add(company);
        else
            db.CachedCompanies.Update(company);

        await db.SaveChangesAsync();
        return company;
    }

    public async Task ReplaceJobsAsync(int cachedCompanyId, List<CachedJob> jobs)
    {
        var old = await db.CachedJobs
            .Where(j => j.CachedCompanyId == cachedCompanyId)
            .ToListAsync();
        db.CachedJobs.RemoveRange(old);

        foreach (var job in jobs)
            job.CachedCompanyId = cachedCompanyId;

        db.CachedJobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }

    public async Task PurgeExpiredJobsAsync()
    {
        var expired = await db.CachedJobs
            .Where(j => j.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();
        if (expired.Count == 0) return;
        db.CachedJobs.RemoveRange(expired);
        await db.SaveChangesAsync();
    }

    public Task<StoreStats> GetStatsAsync() => Task.FromResult(new StoreStats(
        db.LocationSearches.Count(),
        db.CachedCompanies.Count(),
        db.CachedJobs.Count(j => j.ExpiresAt > DateTime.UtcNow),
        db.CachedJobs.Count(j => j.ExpiresAt <= DateTime.UtcNow)));
}
