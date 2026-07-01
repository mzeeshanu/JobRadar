using JobRadar.Models;
using JobRadar.Services;
using System.Text.Json;

namespace JobRadar.Services.Crawling;

/// <summary>
/// Fetches jobs directly from ATS public APIs — structured data, no scraping required.
/// Supports: Greenhouse, Lever, Workday, SmartRecruiters, Ashby, BambooHR.
/// </summary>
public class AtsApiClient(IHttpClientFactory http, ILogger<AtsApiClient> logger)
{
    public async Task<List<CachedJob>> FetchJobsAsync(AtsDetector.AtsInfo ats)
    {
        return ats.Type switch
        {
            "greenhouse"      => await FetchGreenhouseAsync(ats),
            "lever"           => await FetchLeverAsync(ats),
            "workday"         => await FetchWorkdayAsync(ats),
            "smartrecruiters" => await FetchSmartRecruitersAsync(ats),
            "ashby"           => await FetchAshbyAsync(ats),
            "bamboohr"        => await FetchBambooHrAsync(ats),
            "workable"        => await FetchWorkableAsync(ats),
            "breezy"          => await FetchBreezyAsync(ats),
            "recruitee"       => await FetchRecruiteeAsync(ats),
            _                 => []
        };
    }

    // ── Greenhouse ──────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchGreenhouseAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Greenhouse {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
            {
                var title    = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("location", out var loc)
                    ? loc.TryGetProperty("name", out var ln) ? ln.GetString() : null : null;
                var url      = job.TryGetProperty("absolute_url", out var u) ? u.GetString() : null;
                var content  = job.TryGetProperty("content", out var c) ? c.GetString() : null;
                var dept     = job.TryGetProperty("departments", out var depts)
                    ? depts.EnumerateArray().Select(d => d.TryGetProperty("name", out var dn) ? dn.GetString() : null)
                           .FirstOrDefault() : null;

                jobs.Add(BuildJob(title, dept, location, content, url, "greenhouse"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Greenhouse fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Lever ───────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchLeverAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Lever {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            foreach (var job in doc.RootElement.EnumerateArray())
            {
                var text = job.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var url  = job.TryGetProperty("hostedUrl", out var u) ? u.GetString() : null;
                var descr = job.TryGetProperty("descriptionPlain", out var d) ? d.GetString() : null;

                string? team = null, location = null;
                if (job.TryGetProperty("categories", out var cat))
                {
                    team     = cat.TryGetProperty("team",     out var tm) ? tm.GetString() : null;
                    location = cat.TryGetProperty("location", out var lc) ? lc.GetString() : null;
                }

                jobs.Add(BuildJob(text, team, location, descr, url, "lever"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Lever fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Workday ─────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchWorkdayAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var body = JsonSerializer.Serialize(new { appliedFacets = new { }, limit = 50, offset = 0, searchText = "" });
            var resp = await client.PostAsync(ats.ApiUrl,
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Workday {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("jobPostings", out var postings)) return jobs;

            foreach (var job in postings.EnumerateArray())
            {
                var title   = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var loc     = job.TryGetProperty("locationsText", out var l) ? l.GetString() : null;
                var path    = job.TryGetProperty("externalPath", out var u) ? u.GetString() : null;
                var fullUrl = path != null ? new Uri(new Uri(ats.ApiUrl), path).ToString() : null;
                jobs.Add(BuildJob(title, null, loc, null, fullUrl, "workday"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Workday fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── SmartRecruiters ─────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchSmartRecruitersAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("SmartRecruiters {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("content", out var content)) return jobs;

            foreach (var job in content.EnumerateArray())
            {
                var title = job.TryGetProperty("name", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("location", out var loc)
                    ? loc.TryGetProperty("city", out var city) ? city.GetString() : null : null;
                var url = job.TryGetProperty("ref", out var u) ? u.GetString() : null;
                jobs.Add(BuildJob(title, null, location, null, url, "smartrecruiters"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "SmartRecruiters fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Ashby ────────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchAshbyAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Ashby {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("jobPostings", out var postings)) return jobs;

            foreach (var job in postings.EnumerateArray())
            {
                var title    = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("locationName", out var l) ? l.GetString() : null;
                var url      = job.TryGetProperty("jobPostingUrl", out var u) ? u.GetString() : null;
                var isRemote = job.TryGetProperty("isRemote", out var rm) && rm.GetBoolean();
                var dept     = job.TryGetProperty("teamName", out var tn) ? tn.GetString() : null;
                var j        = BuildJob(title, dept, location, null, url, "ashby");
                j.IsRemote   = isRemote;
                jobs.Add(j);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Ashby fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── BambooHR ─────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchBambooHrAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("BambooHR {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var job in doc.RootElement.EnumerateArray())
            {
                var title    = job.TryGetProperty("jobOpeningName", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("location", out var l) ? l.GetString() : null;
                var id       = job.TryGetProperty("id", out var i) ? i.GetString() : null;
                var url      = id != null ? $"https://{ats.Slug}.bamboohr.com/careers/{id}" : null;
                jobs.Add(BuildJob(title, null, location, null, url, "bamboohr"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "BambooHR fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Workable ─────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchWorkableAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.PostAsync(ats.ApiUrl,
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Workable {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("results", out var results)) return jobs;

            foreach (var job in results.EnumerateArray())
            {
                var title    = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("location", out var loc)
                    ? (loc.TryGetProperty("city", out var c) ? c.GetString() : null) : null;
                var url      = job.TryGetProperty("url", out var u) ? u.GetString() : null;
                var remote   = job.TryGetProperty("remote", out var r) && r.GetBoolean();
                var dept     = job.TryGetProperty("department", out var d) ? d.GetString() : null;
                var j        = BuildJob(title, dept, location, null, url, "workable");
                j.IsRemote   = remote;
                jobs.Add(j);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Workable fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Breezy HR ─────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchBreezyAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Breezy {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var list = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("positions", out var p) ? p : default;
            if (list.ValueKind != JsonValueKind.Array) return jobs;

            foreach (var job in list.EnumerateArray())
            {
                var title    = job.TryGetProperty("name", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("location", out var loc)
                    ? loc.TryGetProperty("name", out var ln) ? ln.GetString() : null : null;
                var id       = job.TryGetProperty("_id", out var i) ? i.GetString() : null;
                var url      = id != null ? $"https://{ats.Slug}.breezy.hr/p/{id}" : null;
                jobs.Add(BuildJob(title, null, location, null, url, "breezy"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Breezy fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Recruitee ─────────────────────────────────────────────────────────────
    private async Task<List<CachedJob>> FetchRecruiteeAsync(AtsDetector.AtsInfo ats)
    {
        var jobs = new List<CachedJob>();
        try
        {
            var client = http.CreateClient();
            var resp = await client.GetAsync(ats.ApiUrl);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Recruitee {Status} for {Slug}", (int)resp.StatusCode, ats.Slug);
                return jobs;
            }
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("offers", out var offers)) return jobs;

            foreach (var job in offers.EnumerateArray())
            {
                var title    = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var location = job.TryGetProperty("city", out var c) ? c.GetString() : null;
                var url      = job.TryGetProperty("careers_url", out var u) ? u.GetString() : null;
                var dept     = job.TryGetProperty("department", out var d) ? d.GetString() : null;
                jobs.Add(BuildJob(title, dept, location, null, url, "recruitee"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Recruitee fetch failed for {Slug}", ats.Slug); }
        return jobs;
    }

    // ── Shared builder ───────────────────────────────────────────────────────
    private static CachedJob BuildJob(
        string title, string? department, string? location,
        string? description, string? applyUrl, string source)
    {
        var text = $"{title} {department} {description}";
        return new CachedJob
        {
            Title           = title,
            Department      = department,
            Location        = location,
            Description     = description?.Length > 2000 ? description[..2000] : description,
            ApplyUrl        = applyUrl,
            ExperienceLevel = SkillExtractor.DetectExperienceLevel(text),
            IsRemote        = SkillExtractor.DetectRemote(text),
            Skills          = SkillExtractor.Extract(text),
            SourceProvider  = source,
            DateFound       = DateTime.UtcNow,
        };
    }
}
