using System.Text.Json;

namespace JobRadar.Services.Providers.Jobs;

/// <summary>
/// The Muse API — requires an API key (api_key param). Register free at themuse.com.
/// Configure: Providers:Jobs:TheMuse:ApiKey
/// </summary>
[JobReader("TheMuse",
    "The Muse — tech/startup jobs with company culture info. Requires a free API key.",
    RequiresApiKey = true,
    ApiKeyPath     = "Providers:Jobs:TheMuse:ApiKey")]
public class TheMuseJobProvider(
    IHttpClientFactory http,
    IConfiguration config,
    ILogger<TheMuseJobProvider> logger) : IJobProvider
{
    public string Name        => "The Muse";
    public string Description => "Tech and startup jobs with company culture info. Requires a free API key from themuse.com.";

    private string ApiKey => config["Providers:Jobs:TheMuse:ApiKey"] ?? string.Empty;
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<List<JobResult>> GetJobsAsync(double lat, double lng, int radiusMiles, string? keywords = null)
    {
        if (!IsAvailable) return [];

        var results = new List<JobResult>();
        var client = http.CreateClient();

        var url = $"https://www.themuse.com/api/public/jobs?page=1&descending=true&api_key={Uri.EscapeDataString(ApiKey)}";
        if (!string.IsNullOrWhiteSpace(keywords))
            url += $"&category={Uri.EscapeDataString(keywords)}";

        try
        {
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("The Muse returned {Status} — check your API key", (int)resp.StatusCode);
                return [];
            }

            var json = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            foreach (var job in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var name = job.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var company = job.TryGetProperty("company", out var c)
                    ? (c.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "")
                    : "";

                var locations = job.TryGetProperty("locations", out var locs)
                    ? string.Join(", ", locs.EnumerateArray()
                        .Select(l => l.TryGetProperty("name", out var ln) ? ln.GetString() : null)
                        .Where(l => l != null))
                    : null;

                var contents = job.TryGetProperty("contents", out var cont) ? cont.GetString() : null;
                var applyUrl = job.TryGetProperty("refs", out var refs)
                    ? (refs.TryGetProperty("landing_page", out var lp) ? lp.GetString() : null)
                    : null;

                var fullText = $"{name} {contents}";
                var isRemote = locations?.Contains("Flexible", StringComparison.OrdinalIgnoreCase) == true
                               || SkillExtractor.DetectRemote(locations);

                results.Add(new JobResult(
                    Title: name,
                    CompanyName: company,
                    Location: locations,
                    Description: StripHtml(contents),
                    ApplyUrl: applyUrl,
                    ExperienceLevel: SkillExtractor.DetectExperienceLevel(fullText),
                    IsRemote: isRemote,
                    Skills: SkillExtractor.Extract(fullText),
                    SourceProvider: Name));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The Muse API call failed");
        }

        return results;
    }

    private static string? StripHtml(string? html)
    {
        if (html == null) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ").Trim();
    }
}
