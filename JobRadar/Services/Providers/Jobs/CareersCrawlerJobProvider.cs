namespace JobRadar.Services.Providers.Jobs;

/// <summary>
/// Wraps the existing career page crawler as a job provider.
/// Best used alongside company providers to supply the target URLs.
/// </summary>
[JobReader("CareerPageCrawler", "Crawls company career pages directly. Uses ATS APIs, HTML, and Playwright as fallback.")]
public class CareersCrawlerJobProvider(
    CareersCrawlerService crawler,
    ILogger<CareersCrawlerJobProvider> logger) : IJobProvider
{
    public string Name        => "Career Page Crawler";
    public string Description => "Crawls company career pages via ATS APIs, static HTML, and JS rendering.";
    public bool   IsAvailable => true;

    public async Task<List<JobResult>> GetJobsAsync(double lat, double lng, int radiusMiles, string? keywords = null)
    {
        // This provider is driven by companies discovered separately.
        // When called standalone it returns empty — it's wired in via
        // CompanyDiscoveryOrchestrator which passes company URLs directly.
        logger.LogInformation("CareersCrawlerJobProvider requires companies — use via orchestrator.");
        return await Task.FromResult<List<JobResult>>([]);
    }

    public async Task<List<JobResult>> GetJobsForCompanyAsync(Models.Company company)
    {
        var jobs = await crawler.CrawlCompanyAsync(company);
        return jobs.Select(j => new JobResult(
            Title: j.Title,
            CompanyName: company.Name,
            Location: j.Location,
            Description: j.Description,
            ApplyUrl: j.ApplyUrl,
            ExperienceLevel: j.ExperienceLevel,
            IsRemote: j.IsRemote,
            Skills: j.Skills,
            SourceProvider: Name)).ToList();
    }
}
