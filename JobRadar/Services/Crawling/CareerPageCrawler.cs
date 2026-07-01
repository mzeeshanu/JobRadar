using HtmlAgilityPack;
using JobRadar.Models;
using JobRadar.Services;
using System.Text.RegularExpressions;

namespace JobRadar.Services.Crawling;

/// <summary>
/// Attempts to find a company's careers page from their website,
/// then scrapes job listings using HtmlAgilityPack.
/// Used as the fallback when ATS API detection fails.
/// </summary>
public class CareerPageCrawler(IHttpClientFactory http, ILogger<CareerPageCrawler> logger)
{
    private static readonly string[] CareerPageSuffixes =
    [
        "/careers", "/jobs", "/career", "/openings", "/opportunities",
        "/work-with-us", "/join-us", "/open-roles", "/open-positions",
        "/work-here", "/hiring", "/employment", "/vacancies", "/apply",
        "/about/careers", "/about/jobs", "/company/careers", "/company/jobs",
        "/en/careers", "/en/jobs", "/us/careers",
        "/join-our-team", "/job-openings", "/current-openings"
    ];

    private static readonly string[] CareerLinkPatterns =
    [
        "careers", "jobs", "work-with-us", "join us", "join our team",
        "open roles", "open positions", "we're hiring", "hiring", "apply",
        "openings", "opportunities", "vacancies", "employment"
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Discover and return the most likely careers URL for a website.</summary>
    public async Task<string?> FindCareersUrlAsync(string website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;
        var base_ = NormalizeBase(website);
        if (base_ == null) return null;

        // 1. Try well-known suffixes first (fast, no HTTP needed yet)
        var client = MakeClient();
        foreach (var suffix in CareerPageSuffixes)
        {
            var candidate = base_ + suffix;
            if (await IsReachableAsync(client, candidate)) return candidate;
        }

        // 2. Parse home page and look for careers links
        var homeLink = await FindLinkInPageAsync(client, base_);
        if (homeLink != null) return homeLink;

        return null;
    }

    /// <summary>Scrape jobs from a careers URL. Returns empty list if nothing found.</summary>
    public async Task<List<CachedJob>> ScrapeJobsAsync(string careersUrl)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = MakeClient();
            var html = await client.GetStringAsync(careersUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var root = doc.DocumentNode;

            // Strategy 1: look for repeated structured blocks (role cards)
            jobs.AddRange(ExtractStructuredCards(root, careersUrl));
            if (jobs.Count > 0) return jobs;

            // Strategy 2: look for lists of <a> tags that look like job titles
            jobs.AddRange(ExtractJobLinks(root, careersUrl));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HTML crawl failed for {Url}", careersUrl);
        }
        return jobs;
    }

    // ── Strategy 1 — Structured cards ────────────────────────────────────────

    private static List<CachedJob> ExtractStructuredCards(HtmlNode root, string baseUrl)
    {
        var jobs = new List<CachedJob>();

        // Look for common job-listing data structures
        var candidates = new List<HtmlNodeCollection?>
        {
            // data-attribute based (ATS embeds often use these)
            root.SelectNodes("//*[@data-job-id or @data-role or @data-position or @data-requisition-id or @data-automation='job-item']"),
            // <li> — most job listing pages use unordered lists
            root.SelectNodes("//li[contains(@class,'job') or contains(@class,'role') or contains(@class,'position') or contains(@class,'opening') or contains(@class,'vacancy') or contains(@class,'listing') or contains(@class,'posting')]"),
            // <div> cards
            root.SelectNodes("//div[contains(@class,'job') or contains(@class,'role') or contains(@class,'opening') or contains(@class,'position') or contains(@class,'vacancy') or contains(@class,'listing') or contains(@class,'posting') or contains(@class,'opportunity')]"),
            // <article> semantic cards
            root.SelectNodes("//article[contains(@class,'job') or contains(@class,'role') or contains(@class,'position') or contains(@class,'listing')]"),
            // <section> blocks
            root.SelectNodes("//section[contains(@class,'job') or contains(@class,'role') or contains(@class,'position')]"),
            // <tr> table rows (older enterprise career pages use tables)
            root.SelectNodes("//tr[contains(@class,'job') or contains(@class,'role') or contains(@class,'position') or contains(@class,'opening')]"),
        };

        foreach (var nodeList in candidates.Where(n => n != null))
        {
            foreach (var node in nodeList!.Take(100))
            {
                var job = ParseJobCard(node, baseUrl);
                if (job != null) jobs.Add(job);
            }
            if (jobs.Count > 0) break;
        }

        return Deduplicate(jobs);
    }

    private static CachedJob? ParseJobCard(HtmlNode node, string baseUrl)
    {
        // Title: heading tags within the card, or the node's own text if short
        var titleNode = node.SelectSingleNode(".//h1|.//h2|.//h3|.//h4|.//strong|.//b");
        var title = CleanText(titleNode?.InnerText ?? node.InnerText);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120) return null;

        // Must look like a job title (not generic nav text)
        if (!LooksLikeJobTitle(title)) return null;

        // Apply URL: first <a> within the card
        var link = node.SelectSingleNode(".//a[@href]");
        var applyUrl = ResolveUrl(link?.GetAttributeValue("href", ""), baseUrl);

        // Location hint
        var locationNode = node.SelectSingleNode(
            ".//*[contains(@class,'location') or contains(@class,'loc') or contains(@class,'city')]");
        var location = CleanText(locationNode?.InnerText);

        var text = $"{title} {node.InnerText}";
        return new CachedJob
        {
            Title           = title,
            Location        = location?.Length > 0 ? location : null,
            ApplyUrl        = applyUrl,
            ExperienceLevel = SkillExtractor.DetectExperienceLevel(text),
            IsRemote        = SkillExtractor.DetectRemote(text),
            Skills          = SkillExtractor.Extract(text),
            SourceProvider  = "html",
            DateFound       = DateTime.UtcNow,
        };
    }

    // ── Strategy 2 — Job links ────────────────────────────────────────────────

    private static List<CachedJob> ExtractJobLinks(HtmlNode root, string baseUrl)
    {
        var jobs = new List<CachedJob>();

        // Find all links whose text looks like a job title
        var links = root.SelectNodes("//a[@href]");
        if (links == null) return jobs;

        foreach (var link in links.Take(300))
        {
            var text = CleanText(link.InnerText);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 5 || text.Length > 120) continue;
            if (!LooksLikeJobTitle(text)) continue;

            var href = ResolveUrl(link.GetAttributeValue("href", ""), baseUrl);
            if (href == null) continue;

            jobs.Add(new CachedJob
            {
                Title           = text,
                ApplyUrl        = href,
                ExperienceLevel = SkillExtractor.DetectExperienceLevel(text),
                IsRemote        = SkillExtractor.DetectRemote(text),
                Skills          = SkillExtractor.Extract(text),
                SourceProvider  = "html",
                DateFound       = DateTime.UtcNow,
            });
        }

        return Deduplicate(jobs);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> IsReachableAsync(HttpClient client, string url)
    {
        try
        {
            // HEAD is much faster — only fetches headers, no body download
            using var req  = new HttpRequestMessage(HttpMethod.Head, url);
            var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            // 405 (Method Not Allowed) means the server is alive but doesn't support HEAD —
            // treat as reachable and let the actual GET succeed later
            return resp.IsSuccessStatusCode || (int)resp.StatusCode == 405;
        }
        catch { return false; }
    }

    private async Task<string?> FindLinkInPageAsync(HttpClient client, string baseUrl)
    {
        try
        {
            var html = await client.GetStringAsync(baseUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links == null) return null;

            foreach (var link in links)
            {
                var text = CleanText(link.InnerText).ToLowerInvariant();
                var href = link.GetAttributeValue("href", "");
                if (CareerLinkPatterns.Any(p => text.Contains(p) || href.Contains(p)))
                    return ResolveUrl(href, baseUrl);
            }
        }
        catch { /* best effort */ }
        return null;
    }

    // Role-specific words only — deliberately excludes generic department nouns
    // (support, sales, marketing, operations) which appear on every services page.
    private static readonly Regex JobTitlePattern = new(
        @"\b(engineer|developer|manager|analyst|designer|architect|director|lead|coordinator|" +
        @"specialist|consultant|intern|scientist|researcher|officer|executive|recruiter|" +
        @"software|senior|junior|mid|staff|principal|associate|" +
        @"writer|editor|strategist|administrator|technician|representative|advisor|" +
        @"accountant|attorney|counsel|paralegal|nurse|therapist|physician|" +
        @"technologist|planner|buyer|merchandiser|auditor)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Patterns that indicate service/product names rather than job titles
    private static readonly Regex ServiceNamePattern = new(
        @"\b(services|solutions|support|management|consulting|technologies|platforms?" +
        @"|products?|packages?|plans?|pricing|features?|benefits?|capabilities)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksLikeJobTitle(string text)
    {
        if (!JobTitlePattern.IsMatch(text)) return false;

        var lower = text.ToLowerInvariant();

        // Reject navigation boilerplate
        if (lower.Contains("privacy") || lower.Contains("cookie") ||
            lower.Contains("copyright") || lower.Contains("terms") ||
            lower.Contains("faq") || lower.Contains("contact us"))
            return false;

        // Reject strings that end with service/product nouns — "Remote IT Support Services",
        // "Cloud App Management", "Help Desk Solutions" etc. are service names not job titles
        if (ServiceNamePattern.IsMatch(text)) return false;

        // Reject if it contains a % or $ (metrics / pricing copy)
        if (text.Contains('%') || text.Contains('$') || text.Contains('+')) return false;

        // Reject very long strings — job titles are concise
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8) return false;

        return true;
    }

    private static string CleanText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? ""
        : Regex.Replace(text.Trim(), @"\s+", " ");

    private static string? NormalizeBase(string website)
    {
        try
        {
            if (!website.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                website = "https://" + website;
            var uri = new Uri(website);
            return $"{uri.Scheme}://{uri.Host}";
        }
        catch { return null; }
    }

    private static string? ResolveUrl(string? href, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        try { return new Uri(new Uri(baseUrl), href).ToString(); }
        catch { return null; }
    }

    private static List<CachedJob> Deduplicate(List<CachedJob> jobs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return jobs.Where(j => seen.Add(j.Title)).ToList();
    }

    private HttpClient MakeClient()
    {
        var client = http.CreateClient();
        // Realistic browser UA — many corporate sites block obvious bot strings
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }
}
