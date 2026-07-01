namespace JobRadar.Models;

public class CachedCompany
{
    public int Id { get; set; }
    public int LocationSearchId { get; set; }
    public LocationSearch LocationSearch { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? CareersUrl { get; set; }
    public string? Industry { get; set; }
    public string? SourceProvider { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double DistanceMiles { get; set; }

    // ATS detection
    public string? AtsType { get; set; }   // greenhouse | lever | workday | smartrecruiters | ashby | unknown
    public string? AtsSlug { get; set; }   // company identifier within the ATS

    // Crawl tracking
    public CrawlStatus CrawlStatus { get; set; } = CrawlStatus.Pending;
    public DateTime? LastCrawledAt { get; set; }
    public string? CrawlError { get; set; }

    public List<CachedJob> Jobs { get; set; } = [];

    // Is the job list still fresh?
    public bool JobsAreStale(TimeSpan ttl) =>
        LastCrawledAt == null || DateTime.UtcNow - LastCrawledAt > ttl;
}

public enum CrawlStatus { Pending, Success, Empty, Failed }
