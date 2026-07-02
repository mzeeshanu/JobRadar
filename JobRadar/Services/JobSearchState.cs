using JobRadar.Models;

namespace JobRadar.Services;

/// <summary>
/// Scoped to the Blazor Server circuit — one instance per connected user, not shared
/// app-wide. Location, search status, and results must stay per-visitor: a singleton
/// here would leak one user's search results (and location) to every other visitor.
/// The DB-backed location cache in SearchCacheService is what avoids duplicate crawls
/// for the same coordinates across users; this class is just per-session UI state.
/// </summary>
public class JobSearchState
{
    public List<Company> Companies { get; set; } = [];
    public List<Job> Jobs { get; set; } = [];
    public SearchStatus Status { get; set; } = SearchStatus.Idle;
    public string StatusMessage { get; set; } = string.Empty;
    public double? UserLat { get; set; }
    public double? UserLng { get; set; }
    public string? UserCity { get; set; }
    public int CrawlProgress { get; set; }
    public int TotalToScan { get; set; }
    public bool CompaniesFromCache { get; set; }
    public DateTime? LastCompanyRefresh { get; set; }

    public event Action? OnChange;
    public void NotifyStateChanged() => OnChange?.Invoke();

    // --- Computed stats ---

    public Dictionary<string, int> SkillCounts =>
        Jobs.SelectMany(j => j.Skills)
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> JobsByExperience =>
        Jobs.GroupBy(j => j.ExperienceLevel ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> JobsByCompany =>
        Jobs.GroupBy(j => j.Company?.Name ?? "Unknown")
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

    public int RemoteCount => Jobs.Count(j => j.IsRemote);
}

public enum SearchStatus { Idle, LocatingUser, FindingCompanies, Crawling, Done, Error }
