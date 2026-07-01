using JobRadar.Models;
using JobRadar.Services;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace JobRadar.Services.Crawling;

/// <summary>
/// JS-capable headless Chromium crawler via Playwright.
/// Used when HtmlAgilityPack returns 0 jobs (SPA/React/Vue career pages).
/// Runs headless — no visible window. Shared IPlaywright instance per app lifetime.
/// </summary>
public class PlaywrightCrawler(ILogger<PlaywrightCrawler> logger) : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser?    _browser;
    private readonly SemaphoreSlim _semaphore = new(3); // max 3 concurrent pages
    private readonly SemaphoreSlim _initLock  = new(1, 1);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Render the page with JS, then extract jobs.
    /// Returns empty list (does not throw) if Playwright is unavailable or the page errors.
    /// </summary>
    public async Task<List<CachedJob>> ScrapeJobsAsync(string url)
    {
        await EnsureBrowserAsync();
        if (_browser == null) return [];

        await _semaphore.WaitAsync();
        IPage? page = null;
        try
        {
            page = await _browser.NewPageAsync(new BrowserNewPageOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                            "AppleWebKit/537.36 (KHTML, like Gecko) " +
                            "Chrome/124.0.0.0 Safari/537.36",
                // Reduce fingerprinting noise — we don't need images or fonts
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["Accept-Language"] = "en-US,en;q=0.9"
                }
            });

            // Block heavy assets to speed up load and reduce bandwidth
            await page.RouteAsync("**/*.{png,jpg,jpeg,gif,svg,woff,woff2,ttf,mp4,webp}", r => r.AbortAsync());

            logger.LogInformation("Playwright navigating to {Url}", url);
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout   = 25_000,
            });

            // Wait for network to settle then give JS frameworks time to render
            try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 8_000 }); }
            catch { /* timeout is fine — DOM is already loaded */ }

            await page.WaitForTimeoutAsync(1500);

            // Scroll down to trigger any lazy-loaded job listings
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
            await page.WaitForTimeoutAsync(800);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await page.WaitForTimeoutAsync(800);

            // Try clicking "Load more" / "Show more jobs" buttons if present
            try
            {
                var loadMore = await page.QuerySelectorAsync(
                    "button:has-text('Load more'), button:has-text('Show more'), " +
                    "button:has-text('View more'), a:has-text('Load more jobs')");
                if (loadMore != null)
                {
                    await loadMore.ClickAsync();
                    await page.WaitForTimeoutAsync(1500);
                }
            }
            catch { /* best effort */ }

            var html = await page.ContentAsync();
            return ParseJobsFromHtml(html, url);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Playwright scrape failed for {Url}", url);
            return [];
        }
        finally
        {
            if (page != null) await page.CloseAsync();
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Try to find a careers URL on the website using JS-rendered content.
    /// Falls back to the plain HTML crawler approach but with JS execution.
    /// </summary>
    public async Task<string?> FindCareersUrlAsync(string website)
    {
        await EnsureBrowserAsync();
        if (_browser == null) return null;

        await _semaphore.WaitAsync();
        IPage? page = null;
        try
        {
            page = await _browser.NewPageAsync();
            await page.RouteAsync("**/*.{png,jpg,gif,woff,woff2,ttf,mp4}", r => r.AbortAsync());

            var baseUrl = NormalizeBase(website);
            if (baseUrl == null) return null;

            await page.GotoAsync(baseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout   = 15_000
            });

            // Query all <a> tags and look for career-link text/href
            var careerLink = await page.EvaluateAsync<string?>(@"() => {
                const patterns = ['careers', 'jobs', 'work-with-us', 'join-us', 'open-roles', 'hiring'];
                for (const a of document.querySelectorAll('a[href]')) {
                    const text = (a.textContent || '').toLowerCase().trim();
                    const href = (a.getAttribute('href') || '').toLowerCase();
                    if (patterns.some(p => text.includes(p) || href.includes(p))) {
                        const resolved = new URL(a.href, document.baseURI).href;
                        return resolved;
                    }
                }
                return null;
            }");

            return careerLink;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Playwright careers-URL search failed for {Website}", website);
            return null;
        }
        finally
        {
            if (page != null) await page.CloseAsync();
            _semaphore.Release();
        }
    }

    // ── HTML parsing (runs on the JS-rendered content) ────────────────────────

    private static readonly Regex JobTitlePattern = new(
        @"\b(engineer|developer|manager|analyst|designer|architect|director|lead|" +
        @"coordinator|specialist|consultant|intern|scientist|researcher|officer|" +
        @"executive|software|senior|junior|mid|staff|principal|associate|" +
        @"writer|editor|strategist|administrator|technician|representative|advisor|" +
        @"accountant|attorney|counsel|paralegal|nurse|therapist|physician|" +
        @"technologist|planner|buyer|merchandiser|auditor)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ServiceNamePattern = new(
        @"\b(services|solutions|support|management|consulting|technologies|platforms?" +
        @"|products?|packages?|plans?|pricing|features?|benefits?|capabilities)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private List<CachedJob> ParseJobsFromHtml(string html, string baseUrl)
    {
        var jobs = new List<CachedJob>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Use regex on the rendered HTML — avoids HtmlAgilityPack dependency here
        // and handles malformed HTML that SPAs sometimes produce mid-render.
        var linkMatches = Regex.Matches(html,
            @"<a[^>]+href=""([^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in linkMatches.Take(500))
        {
            var rawText = StripTags(m.Groups[2].Value).Trim();
            if (rawText.Length < 5 || rawText.Length > 100) continue;
            if (!JobTitlePattern.IsMatch(rawText)) continue;
            if (ServiceNamePattern.IsMatch(rawText)) continue;
            if (rawText.Contains('%') || rawText.Contains('$') || rawText.Contains('+')) continue;
            if (rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8) continue;
            if (!seen.Add(rawText)) continue;

            var href    = ResolveUrl(m.Groups[1].Value, baseUrl);
            var context = GetSurroundingText(html, m.Index, 300);

            jobs.Add(new CachedJob
            {
                Title           = rawText,
                ApplyUrl        = href,
                ExperienceLevel = SkillExtractor.DetectExperienceLevel(context),
                IsRemote        = SkillExtractor.DetectRemote(context),
                Skills          = SkillExtractor.Extract(context),
                SourceProvider  = "playwright",
                DateFound       = DateTime.UtcNow,
            });
        }

        logger.LogInformation("Playwright extracted {Count} jobs from {Url}", jobs.Count, baseUrl);
        return jobs;
    }

    private static string StripTags(string html) =>
        Regex.Replace(html, "<[^>]+>", " ").Trim();

    private static string GetSurroundingText(string html, int index, int radius)
    {
        var start = Math.Max(0, index - radius);
        var end   = Math.Min(html.Length, index + radius);
        return StripTags(html[start..end]);
    }

    private static string? ResolveUrl(string href, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        try { return new Uri(new Uri(baseUrl), href).ToString(); }
        catch { return null; }
    }

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

    // ── Browser lifecycle ─────────────────────────────────────────────────────

    private async Task EnsureBrowserAsync()
    {
        if (_browser != null) return;
        await _initLock.WaitAsync();
        try
        {
            if (_browser != null) return;
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args     = ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"],
            });
            logger.LogInformation("Playwright Chromium browser launched");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Playwright browser launch failed — JS crawling disabled");
            _playwright?.Dispose();
            _playwright = null;
        }
        finally { _initLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
