using JobRadar.Models;

namespace JobRadar.Services.Crawling;

/// <summary>
/// Orchestrates the full job-discovery pipeline for a single company:
///   1. ATS detection  — URL pattern match (instant)
///   2. ATS API call   — structured JSON, best quality
///   3. HTML crawl     — HtmlAgilityPack, works for static pages
///   4. Playwright     — headless Chromium, handles React/Vue SPAs
/// </summary>
public class CompanyCrawlerService(
    AtsApiClient atsApi,
    CareerPageCrawler htmlCrawler,
    PlaywrightCrawler jsCrawler,
    SearchCacheService cache,
    ILogger<CompanyCrawlerService> logger)
{
    public async Task<List<CachedJob>> CrawlCompanyAsync(CachedCompany company)
    {
        // ── Step 1: ATS detection ─────────────────────────────────────────────
        var atsInfo = AtsDetector.Detect(company.CareersUrl, company.Website);

        if (atsInfo != null)
        {
            company.AtsType = atsInfo.Type;
            company.AtsSlug = atsInfo.Slug;
            await cache.UpdateCompanyCareersInfoAsync(company);

            logger.LogInformation("ATS detected for {Company}: {Type} (slug={Slug})",
                company.Name, atsInfo.Type, atsInfo.Slug);

            // ── Step 2: ATS API call ──────────────────────────────────────────
            var atsJobs = await atsApi.FetchJobsAsync(atsInfo);
            if (atsJobs.Count > 0)
            {
                await cache.SaveJobsAsync(company, atsJobs, atsInfo.Type);
                return atsJobs;
            }

            logger.LogInformation("ATS API returned 0 jobs for {Company} — falling through to HTML crawl", company.Name);
        }

        // ── Step 3: Discover careers URL if missing ───────────────────────────
        var careersUrl = company.CareersUrl;
        if (string.IsNullOrWhiteSpace(careersUrl))
        {
            careersUrl = await htmlCrawler.FindCareersUrlAsync(company.Website ?? "")
                      ?? await jsCrawler.FindCareersUrlAsync(company.Website ?? "");

            if (careersUrl != null)
            {
                company.CareersUrl = careersUrl;
                await cache.UpdateCompanyCareersInfoAsync(company);
                logger.LogInformation("Discovered careers URL for {Company}: {Url}", company.Name, careersUrl);

                // Re-run ATS detection now that we have the actual careers URL —
                // most ATS boards are at /careers or a subdomain, not the root site.
                if (atsInfo == null)
                {
                    atsInfo = AtsDetector.Detect(careersUrl, company.Website);
                    if (atsInfo != null)
                    {
                        company.AtsType = atsInfo.Type;
                        company.AtsSlug = atsInfo.Slug;
                        await cache.UpdateCompanyCareersInfoAsync(company);
                        logger.LogInformation("ATS detected (post-discovery) for {Company}: {Type}", company.Name, atsInfo.Type);

                        var atsJobs2 = await atsApi.FetchJobsAsync(atsInfo);
                        if (atsJobs2.Count > 0)
                        {
                            await cache.SaveJobsAsync(company, atsJobs2, atsInfo.Type);
                            return atsJobs2;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(careersUrl))
        {
            await cache.MarkCrawlFailedAsync(company, "No careers URL found");
            return [];
        }

        // ── Step 4: Static HTML crawl ─────────────────────────────────────────
        var htmlJobs = await htmlCrawler.ScrapeJobsAsync(careersUrl);
        if (htmlJobs.Count > 0)
        {
            await cache.SaveJobsAsync(company, htmlJobs, "html");
            return htmlJobs;
        }

        // ── Step 5: Playwright JS crawl (SPA fallback) ────────────────────────
        logger.LogInformation("HTML crawl returned 0 jobs for {Company} — trying Playwright", company.Name);
        var jsJobs = await jsCrawler.ScrapeJobsAsync(careersUrl);
        if (jsJobs.Count > 0)
        {
            await cache.SaveJobsAsync(company, jsJobs, "playwright");
            return jsJobs;
        }

        await cache.MarkCrawlFailedAsync(company, "No jobs found via ATS API, HTML crawl, or Playwright");
        return [];
    }
}
