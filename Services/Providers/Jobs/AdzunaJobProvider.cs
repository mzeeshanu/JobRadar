using System.Text.Json;

namespace JobRadar.Services.Providers.Jobs;

/// <summary>
/// Adzuna Jobs API — free, 1000 calls/day. Register at developer.adzuna.com.
/// </summary>
[JobReader("Adzuna", "Adzuna Jobs API — location-aware, 1000 free calls/day. Register at developer.adzuna.com.",
    RequiresApiKey = true, ApiKeyPath = "Providers:Jobs:Adzuna:ApiKey")]
public class AdzunaJobProvider(
    IHttpClientFactory http,
    IConfiguration config,
    ILogger<AdzunaJobProvider> logger) : IJobProvider
{
    public string Name        => "Adzuna";
    public string Description => "Location-aware job board. 1000 free API calls/day. Requires App ID + API key.";
    public bool   IsAvailable => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(ApiKey);

    private string AppId   => config["Providers:Jobs:Adzuna:AppId"]  ?? string.Empty;
    private string ApiKey  => config["Providers:Jobs:Adzuna:ApiKey"]  ?? string.Empty;
    private string Country => config["Providers:Jobs:Adzuna:Country"] ?? "us";

    public async Task<List<JobResult>> GetJobsAsync(double lat, double lng, int radiusMiles, string? keywords = null)
    {
        if (string.IsNullOrWhiteSpace(AppId) || string.IsNullOrWhiteSpace(ApiKey))
        {
            logger.LogInformation("Adzuna credentials not configured — skipping.");
            return [];
        }

        var results = new List<JobResult>();
        var client = http.CreateClient();

        var url = $"https://api.adzuna.com/v1/api/jobs/{Country}/search/1" +
                  $"?app_id={AppId}&app_key={ApiKey}" +
                  $"&where={lat},{lng}&distance={radiusMiles}" +
                  $"&results_per_page=50&content-type=application/json";

        if (!string.IsNullOrWhiteSpace(keywords))
            url += $"&what={Uri.EscapeDataString(keywords)}";

        try
        {
            var json = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            foreach (var job in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var title = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var company = job.TryGetProperty("company", out var c)
                    ? (c.TryGetProperty("display_name", out var cn) ? cn.GetString() ?? "" : "")
                    : "";
                var location = job.TryGetProperty("location", out var loc)
                    ? (loc.TryGetProperty("display_name", out var ld) ? ld.GetString() : null)
                    : null;
                var description = job.TryGetProperty("description", out var d) ? d.GetString() : null;
                var applyUrl = job.TryGetProperty("redirect_url", out var u) ? u.GetString() : null;

                var fullText = $"{title} {description}";
                results.Add(new JobResult(
                    Title: title,
                    CompanyName: company,
                    Location: location,
                    Description: description,
                    ApplyUrl: applyUrl,
                    ExperienceLevel: SkillExtractor.DetectExperienceLevel(fullText),
                    IsRemote: SkillExtractor.DetectRemote(fullText),
                    Skills: SkillExtractor.Extract(fullText),
                    SourceProvider: Name));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Adzuna API call failed");
        }

        return results;
    }
}
