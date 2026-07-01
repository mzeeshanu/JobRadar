using System.Text.RegularExpressions;

namespace JobRadar.Services.Crawling;

/// <summary>
/// Detects which Applicant Tracking System a company uses from their careers page URL.
/// Each major ATS exposes a public jobs API — no scraping needed once detected.
/// Call Detect() with both the careers URL and the root website for best coverage.
/// </summary>
public static class AtsDetector
{
    public record AtsInfo(string Type, string Slug, string ApiUrl);

    public static AtsInfo? Detect(string? careersUrl, string? website = null)
    {
        foreach (var url in new[] { careersUrl, website }.Where(u => !string.IsNullOrEmpty(u)))
        {
            var result = TryDetect(url!);
            if (result != null) return result;
        }
        return null;
    }

    private static AtsInfo? TryDetect(string url)
    {
        // Greenhouse  — boards.greenhouse.io/{slug}  OR  {company}.greenhouse.io
        var gh = Match(url, @"greenhouse\.io(?:/careers)?/(?:embed/job_board\?for=)?([a-zA-Z0-9_\-]+)");
        if (gh != null)
            return new AtsInfo("greenhouse", gh,
                $"https://boards.greenhouse.io/api/v1/boards/{gh}/jobs?content=true");

        // Lever  — jobs.lever.co/{slug}
        var lv = Match(url, @"jobs\.lever\.co/([a-zA-Z0-9_\-]+)");
        if (lv != null)
            return new AtsInfo("lever", lv,
                $"https://api.lever.co/v0/postings/{lv}?mode=json");

        // Workday  — {company}.wd{N}.myworkdayjobs.com/{tenant}/...
        if (Regex.IsMatch(url, @"\.wd\d+\.myworkdayjobs\.com", RegexOptions.IgnoreCase))
        {
            var uri     = new Uri(url);
            var company = uri.Host.Split('.')[0];
            // Tenant is the first non-empty path segment (e.g. /en-US/AcmeCorp_External → AcmeCorp_External)
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var tenant   = segments.FirstOrDefault(s =>
                !s.Equals("jobs", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(s, @"^[a-z]{2}-[A-Z]{2}$")) // skip locale like en-US
                ?? company;
            return new AtsInfo("workday", company,
                $"https://{uri.Host}/wday/cxs/{tenant}/jobs");
        }

        // SmartRecruiters  — careers.smartrecruiters.com/{slug}
        var sr = Match(url, @"smartrecruiters\.com/([a-zA-Z0-9_\-]+)");
        if (sr != null)
            return new AtsInfo("smartrecruiters", sr,
                $"https://api.smartrecruiters.com/v1/companies/{sr}/postings");

        // Ashby  — jobs.ashbyhq.com/{slug}
        var ab = Match(url, @"ashbyhq\.com/([a-zA-Z0-9_\-]+)");
        if (ab != null)
            return new AtsInfo("ashby", ab,
                $"https://api.ashbyhq.com/posting-api/job-board/{ab}");

        // BambooHR  — {company}.bamboohr.com/careers
        var bhr = Match(url, @"([a-zA-Z0-9_\-]+)\.bamboohr\.com");
        if (bhr != null)
            return new AtsInfo("bamboohr", bhr,
                $"https://{bhr}.bamboohr.com/careers/list");

        // Rippling  — app.rippling.com/jobs/{slug}
        var rp = Match(url, @"rippling\.com/jobs/([a-zA-Z0-9_\-]+)");
        if (rp != null)
            return new AtsInfo("rippling", rp,
                $"https://app.rippling.com/api/o/jobs/job-postings/?company={rp}");

        // Workable  — apply.workable.com/{slug}  OR  {company}.workable.com
        var wb = Match(url, @"(?:apply\.)?workable\.com/([a-zA-Z0-9_\-]+)");
        if (wb != null)
            return new AtsInfo("workable", wb,
                $"https://apply.workable.com/api/v3/accounts/{wb}/jobs");

        // Jobvite  — jobs.jobvite.com/{slug}
        var jv = Match(url, @"jobvite\.com/([a-zA-Z0-9_\-]+)");
        if (jv != null)
            return new AtsInfo("jobvite", jv,
                $"https://api.jobvite.com/api/job?api={jv}&callback=");

        // Breezy HR  — {slug}.breezy.hr
        var bz = Match(url, @"([a-zA-Z0-9_\-]+)\.breezy\.hr");
        if (bz != null)
            return new AtsInfo("breezy", bz,
                $"https://{bz}.breezy.hr/json");

        // Recruitee  — {slug}.recruitee.com
        var rt = Match(url, @"([a-zA-Z0-9_\-]+)\.recruitee\.com");
        if (rt != null)
            return new AtsInfo("recruitee", rt,
                $"https://api.recruitee.com/c/{rt}/positions");

        // JazzHR  — {slug}.jazz.co
        var jz = Match(url, @"([a-zA-Z0-9_\-]+)\.jazz\.co");
        if (jz != null)
            return new AtsInfo("jazzhr", jz,
                $"https://api.jazz.co/api/jobs?apikey=&subdomain={jz}");

        return null;
    }

    private static string? Match(string url, string pattern)
    {
        var m = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
