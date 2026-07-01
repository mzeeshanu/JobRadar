using JobRadar.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobRadar.Services.Storage;

/// <summary>
/// Flat-file JSON store. Zero dependencies — no database required.
/// All data is kept in a single jobradar-cache.json file.
/// Configure: Storage:Provider = "json"
///            Storage:JsonFile:Path = "jobradar-cache.json"  (optional, defaults to app dir)
/// </summary>
public class JsonFileJobRadarStore : IJobRadarStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public JsonFileJobRadarStore(IConfiguration config)
    {
        _filePath = config["Storage:JsonFile:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "jobradar-cache.json");
    }

    // ── IJobRadarStore ────────────────────────────────────────────────────────

    public async Task<LocationSearch?> FindSearchAsync(double lat, double lng, int radiusMeters)
    {
        var db = await LoadAsync();
        return db.Searches.FirstOrDefault(s =>
            Math.Abs(s.Lat - lat) < 0.01 &&
            Math.Abs(s.Lng - lng) < 0.01 &&
            s.RadiusMeters == radiusMeters);
    }

    public async Task<LocationSearch> SaveSearchAsync(LocationSearch search)
    {
        await _lock.WaitAsync();
        try
        {
            var db = await LoadAsync();
            var existing = db.Searches.FirstOrDefault(s =>
                Math.Abs(s.Lat - search.Lat) < 0.01 &&
                Math.Abs(s.Lng - search.Lng) < 0.01 &&
                s.RadiusMeters == search.RadiusMeters);

            if (existing != null)
            {
                existing.LastRefreshedAt = search.LastRefreshedAt;
                await SaveAsync(db);
                return existing;
            }

            search.Id = db.NextSearchId++;
            db.Searches.Add(search);
            await SaveAsync(db);
            return search;
        }
        finally { _lock.Release(); }
    }

    public async Task<CachedCompany?> FindCompanyAsync(int locationSearchId, string name)
    {
        var db = await LoadAsync();
        var company = db.Companies.FirstOrDefault(c =>
            c.LocationSearchId == locationSearchId && c.Name == name);
        if (company != null)
            company.Jobs = db.Jobs.Where(j => j.CachedCompanyId == company.Id).ToList();
        return company;
    }

    public async Task<CachedCompany> SaveCompanyAsync(CachedCompany company)
    {
        await _lock.WaitAsync();
        try
        {
            var db = await LoadAsync();
            var existing = db.Companies.FirstOrDefault(c =>
                c.LocationSearchId == company.LocationSearchId && c.Name == company.Name);

            if (existing != null)
            {
                // Update fields in-place
                existing.Website        = company.Website        ?? existing.Website;
                existing.CareersUrl     = company.CareersUrl     ?? existing.CareersUrl;
                existing.Industry       = company.Industry       ?? existing.Industry;
                existing.AtsType        = company.AtsType        ?? existing.AtsType;
                existing.AtsSlug        = company.AtsSlug        ?? existing.AtsSlug;
                existing.CrawlStatus    = company.CrawlStatus;
                existing.LastCrawledAt  = company.LastCrawledAt  ?? existing.LastCrawledAt;
                existing.CrawlError     = company.CrawlError;
                existing.DistanceMiles  = company.DistanceMiles;
                existing.SourceProvider = company.SourceProvider ?? existing.SourceProvider;
                await SaveAsync(db);
                return existing;
            }

            company.Id = db.NextCompanyId++;
            db.Companies.Add(company);
            await SaveAsync(db);
            return company;
        }
        finally { _lock.Release(); }
    }

    public async Task ReplaceJobsAsync(int cachedCompanyId, List<CachedJob> jobs)
    {
        await _lock.WaitAsync();
        try
        {
            var db = await LoadAsync();
            db.Jobs.RemoveAll(j => j.CachedCompanyId == cachedCompanyId);
            foreach (var job in jobs)
            {
                job.Id              = db.NextJobId++;
                job.CachedCompanyId = cachedCompanyId;
            }
            db.Jobs.AddRange(jobs);
            await SaveAsync(db);
        }
        finally { _lock.Release(); }
    }

    public async Task PurgeExpiredJobsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var db = await LoadAsync();
            var before = db.Jobs.Count;
            db.Jobs.RemoveAll(j => j.IsExpired);
            if (db.Jobs.Count != before) await SaveAsync(db);
        }
        finally { _lock.Release(); }
    }

    public async Task<StoreStats> GetStatsAsync()
    {
        var db = await LoadAsync();
        return new StoreStats(
            db.Searches.Count,
            db.Companies.Count,
            db.Jobs.Count(j => !j.IsExpired),
            db.Jobs.Count(j => j.IsExpired));
    }

    // ── Internal file I/O ─────────────────────────────────────────────────────

    private async Task<JsonDb> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new JsonDb();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<JsonDb>(json, JsonOpts) ?? new JsonDb();
        }
        catch { return new JsonDb(); }
    }

    private async Task SaveAsync(JsonDb db)
    {
        var json = JsonSerializer.Serialize(db, JsonOpts);
        // Write atomically via temp file + rename
        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, _filePath, overwrite: true);
    }

    // ── JSON document model ───────────────────────────────────────────────────

    private class JsonDb
    {
        public int NextSearchId  { get; set; } = 1;
        public int NextCompanyId { get; set; } = 1;
        public int NextJobId     { get; set; } = 1;
        public List<LocationSearch>  Searches  { get; set; } = [];
        public List<CachedCompany>   Companies { get; set; } = [];
        public List<CachedJob>       Jobs      { get; set; } = [];
    }
}
