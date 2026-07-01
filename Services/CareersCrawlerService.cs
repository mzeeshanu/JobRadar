using HtmlAgilityPack;
using JobRadar.Models;

namespace JobRadar.Services;

public class CareersCrawlerService(IHttpClientFactory httpFactory, ILogger<CareersCrawlerService> logger)
{
    private static readonly string[] CareersPaths =
        ["/careers", "/jobs", "/careers/jobs", "/join-us", "/work-with-us", "/opportunities", "/hiring"];

    private static readonly string[] JobLinkPatterns =
        ["job", "position", "opening", "role", "career", "opportunity", "posting"];

    public async Task<List<Job>> CrawlCompanyAsync(Company company)
    {
        var careersUrl = await FindCareersPageAsync(company);
        if (careersUrl == null) return [];

        company.CareersUrl = careersUrl;
        company.LastCrawled = DateTime.UtcNow;

        return await ExtractJobsFromPageAsync(company, careersUrl);
    }

    private async Task<string?> FindCareersPageAsync(Company company)
    {
        if (!string.IsNullOrEmpty(company.CareersUrl)) return company.CareersUrl;
        if (string.IsNullOrEmpty(company.Website)) return null;

        var baseUrl = company.Website.TrimEnd('/');
        var client = httpFactory.CreateClient("Crawler");

        // Try known career path patterns
        foreach (var path in CareersPaths)
        {
            try
            {
                var url = baseUrl + path;
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Found careers page for {Company}: {Url}", company.Name, url);
                    return url;
                }
            }
            catch { /* try next */ }
        }

        // Fall back: look for links on the homepage
        try
        {
            var html = await client.GetStringAsync(baseUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (JobLinkPatterns.Any(p => href.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        return href.StartsWith("http") ? href : baseUrl + href;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not parse homepage for {Company}", company.Name);
        }

        return null;
    }

    private async Task<List<Job>> ExtractJobsFromPageAsync(Company company, string careersUrl)
    {
        var jobs = new List<Job>();
        try
        {
            var client = httpFactory.CreateClient("Crawler");
            var html = await client.GetStringAsync(careersUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Strategy: find job-like headings and their surrounding context
            var candidates = FindJobNodes(doc);

            foreach (var (titleNode, contextHtml) in candidates.Take(50))
            {
                var title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());
                if (string.IsNullOrWhiteSpace(title) || title.Length > 120) continue;

                var applyLink = FindApplyLink(titleNode, doc, careersUrl);
                var description = StripHtml(contextHtml);

                jobs.Add(new Job
                {
                    CompanyId = company.Id,
                    Company = company,
                    Title = title,
                    ApplyUrl = applyLink,
                    Description = description,
                    Skills = SkillExtractor.Extract(title + " " + description),
                    ExperienceLevel = SkillExtractor.DetectExperienceLevel(title + " " + description),
                    IsRemote = SkillExtractor.DetectRemote(title + " " + description),
                    DateFound = DateTime.UtcNow,
                    Location = company.Name,
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to crawl {Url}", careersUrl);
        }

        return jobs;
    }

    private static List<(HtmlNode TitleNode, string ContextHtml)> FindJobNodes(HtmlDocument doc)
    {
        var results = new List<(HtmlNode, string)>();

        // Look for list items / divs with job-like links
        var selectors = new[]
        {
            "//li[.//a[contains(@href,'job') or contains(@href,'position') or contains(@href,'role')]]",
            "//div[contains(@class,'job') or contains(@class,'position') or contains(@class,'opening') or contains(@class,'role')]",
            "//article[contains(@class,'job') or contains(@class,'position')]",
            "//h2[following-sibling::a[contains(@href,'apply') or contains(@href,'job')]]",
            "//h3[following-sibling::a]",
        };

        foreach (var selector in selectors)
        {
            var nodes = doc.DocumentNode.SelectNodes(selector);
            if (nodes == null) continue;

            foreach (var node in nodes)
            {
                var titleNode = node.SelectSingleNode(".//a") ?? node.SelectSingleNode(".//h2") ?? node.SelectSingleNode(".//h3") ?? node;
                results.Add((titleNode, node.OuterHtml));
            }

            if (results.Count >= 30) break;
        }

        // Fallback: just grab all job-like links
        if (results.Count == 0)
        {
            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    var text = link.InnerText.Trim();
                    if (text.Length > 5 && text.Length < 100 &&
                        (JobLinkPatterns.Any(p => href.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
                         IsLikelyJobTitle(text)))
                    {
                        results.Add((link, link.ParentNode?.OuterHtml ?? link.OuterHtml));
                    }
                }
            }
        }

        return results.DistinctBy(r => r.Item1.InnerText.Trim()).ToList();
    }

    private static bool IsLikelyJobTitle(string text)
    {
        var titleWords = new[] { "engineer", "developer", "manager", "analyst", "designer", "director",
                                  "specialist", "coordinator", "architect", "consultant", "lead", "scientist" };
        return titleWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindApplyLink(HtmlNode jobNode, HtmlDocument doc, string baseUrl)
    {
        var link = jobNode as HtmlNode ?? jobNode.SelectSingleNode(".//a[@href]");
        if (link != null)
        {
            var href = link.GetAttributeValue("href", "");
            if (!string.IsNullOrEmpty(href))
                return href.StartsWith("http") ? href : new Uri(new Uri(baseUrl), href).ToString();
        }
        return null;
    }

    private static string StripHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return HtmlEntity.DeEntitize(doc.DocumentNode.InnerText.Trim());
    }
}
