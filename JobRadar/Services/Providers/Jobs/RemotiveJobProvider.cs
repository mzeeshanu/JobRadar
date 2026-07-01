using System.Text.Json;

namespace JobRadar.Services.Providers.Jobs;

/// <summary>
/// Remotive API — completely free, no key, no signup. Remote tech jobs only.
/// </summary>
[JobReader("Remotive", "Remotive API — free, no key. Remote tech jobs aggregated from across the web.")]
public class RemotiveJobProvider(IHttpClientFactory http, ILogger<RemotiveJobProvider> logger) : IJobProvider
{
    public string Name        => "Remotive";
    public string Description => "Free remote tech job board. No API key required.";
    public bool   IsAvailable => true;

    public async Task<List<JobResult>> GetJobsAsync(double lat, double lng, int radiusMiles, string? keywords = null)
    {
        var results = new List<JobResult>();
        var client = http.CreateClient();

        var url = "https://remotive.com/api/remote-jobs?limit=50";
        if (!string.IsNullOrWhiteSpace(keywords))
            url += $"&search={Uri.EscapeDataString(keywords)}";

        try
        {
            var json = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
            {
                var title = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var company = job.TryGetProperty("company_name", out var c) ? c.GetString() ?? "" : "";
                var description = job.TryGetProperty("description", out var d) ? d.GetString() : null;
                var applyUrl = job.TryGetProperty("url", out var u) ? u.GetString() : null;
                var category = job.TryGetProperty("category", out var cat) ? cat.GetString() : null;

                var fullText = $"{title} {category} {description}";
                results.Add(new JobResult(
                    Title: title,
                    CompanyName: company,
                    Location: "Remote",
                    Description: StripHtml(description),
                    ApplyUrl: applyUrl,
                    ExperienceLevel: SkillExtractor.DetectExperienceLevel(fullText),
                    IsRemote: true,
                    Skills: SkillExtractor.Extract(fullText),
                    SourceProvider: Name));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Remotive API call failed");
        }

        return results;
    }

    private static string? StripHtml(string? html)
    {
        if (html == null) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ").Trim();
    }
}
