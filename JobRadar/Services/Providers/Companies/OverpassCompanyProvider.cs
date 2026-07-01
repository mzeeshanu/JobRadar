using System.Text.Json;
using System.Web;

namespace JobRadar.Services.Providers.Companies;

/// <summary>
/// OpenStreetMap Overpass API — completely free, no key required.
/// </summary>
[PlaceReader("OpenStreetMapOverpass",
    "Queries OpenStreetMap for nearby offices and businesses. Free, no API key needed.")]
public class OverpassCompanyProvider(
    IHttpClientFactory http,
    IConfiguration config,
    ILogger<OverpassCompanyProvider> logger) : ICompanyProvider
{
    public string Name        => "OpenStreetMap (Overpass)";
    public string Description => "Free OpenStreetMap data — offices, businesses, universities nearby.";
    public bool   IsAvailable => true;

    private static readonly string[] DefaultOsmFilters =
    [
        "office=it", "office=company", "office=financial",
        "amenity=university", "amenity=hospital",
        "office=software", "office=technology"
    ];

    private string[] OsmFilters =>
        config.GetSection("Search:OsmFilters").Get<string[]>() ?? DefaultOsmFilters;

    public async Task<List<CompanyResult>> GetCompaniesAsync(double lat, double lng, int radiusMeters)
    {
        var results = new List<CompanyResult>();

        // Use a plain HttpClient with a neutral User-Agent — Overpass rejects bot UAs
        var client = http.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", "JobRadar/1.0 (educational project)");
        client.Timeout = TimeSpan.FromSeconds(20);

        foreach (var filter in OsmFilters)
        {
            var query = $"[out:json][timeout:10];node[{filter}](around:{radiusMeters},{lat},{lng});out body 20;";
            var url = "https://overpass-api.de/api/interpreter?data=" + HttpUtility.UrlEncode(query);

            try
            {
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Overpass returned {Status} for filter {Filter}", (int)resp.StatusCode, filter);
                    if ((int)resp.StatusCode == 429)
                        await Task.Delay(2000); // back off on rate limit
                    continue;
                }

                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("elements", out var elements)) continue;

                foreach (var element in elements.EnumerateArray())
                {
                    if (!element.TryGetProperty("tags", out var tags)) continue;

                    var name = GetTag(tags, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var elLat = element.GetProperty("lat").GetDouble();
                    var elLng = element.GetProperty("lon").GetDouble();
                    var website = NormalizeUrl(GetTag(tags, "website") ?? GetTag(tags, "contact:website"));

                    results.Add(new CompanyResult(
                        Name: name,
                        Website: website,
                        Industry: filter.Split('=')[1],
                        Latitude: elLat,
                        Longitude: elLng,
                        DistanceMiles: Haversine(lat, lng, elLat, elLng),
                        SourceProvider: Name));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Overpass query failed for filter {Filter}", filter);
            }

            await Task.Delay(500); // stay under Overpass rate limit (2 req/s)
        }

        return results
            .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.DistanceMiles)
            .Take(40)
            .ToList();
    }

    private static string? GetTag(JsonElement tags, string key) =>
        tags.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static string? NormalizeUrl(string? url)
    {
        if (url == null) return null;
        return url.StartsWith("http") ? url : "https://" + url;
    }

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3959;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
