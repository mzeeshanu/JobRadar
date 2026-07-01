using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobRadar.Services.Providers.Companies;

/// <summary>
/// Google Places API (New) — Nearby Search v1.
/// Uses a single POST per keyword; website is returned in the same response
/// (no separate Place Details call needed, halving API usage).
///
/// Enable: Providers:Companies:GooglePlaces:Enabled = true
/// Key:    Providers:Companies:GooglePlaces:ApiKey  = AIza...
///
/// Free $200/month credit ≈ 2,800 Nearby Search calls (at $0.032/call with website field).
/// For dev/demo you will not exceed the free tier.
/// </summary>
[PlaceReader("GooglePlaces",
    "Google Places API (New) — 200M places, website in one call. Requires an API key.",
    RequiresApiKey = true,
    ApiKeyPath     = "Providers:Companies:GooglePlaces:ApiKey")]
public class GooglePlacesCompanyProvider(
    IHttpClientFactory http,
    IConfiguration config,
    ILogger<GooglePlacesCompanyProvider> logger) : ICompanyProvider
{
    public string Name        => "Google Places";
    public string Description => "Google Places (New API) — 200M businesses, website URLs included. Requires API key.";
    public bool   IsAvailable => !string.IsNullOrWhiteSpace(ApiKey);

    private string ApiKey => config["Providers:Companies:GooglePlaces:ApiKey"] ?? string.Empty;

    private static readonly string[] DefaultKeywords =
    [
        "software company", "technology company", "IT company",
        "financial services", "healthcare company", "engineering firm",
        "consulting firm", "startup", "corporation headquarters"
    ];

    private string[] SearchKeywords =>
        config.GetSection("Search:Keywords").Get<string[]>() ?? DefaultKeywords;

    // Text Search supports keyword + location restriction in one call
    private const string SearchUrl =
        "https://places.googleapis.com/v1/places:searchText";

    public async Task<List<CompanyResult>> GetCompaniesAsync(double lat, double lng, int radiusMeters)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            logger.LogInformation("Google Places API key not set — skipping.");
            return [];
        }

        var results = new List<CompanyResult>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client  = http.CreateClient();

        // New API uses X-Goog-Api-Key header instead of ?key= query param
        client.DefaultRequestHeaders.Add("X-Goog-Api-Key", ApiKey);
        // FieldMask tells the API exactly which fields to return — controls billing
        // website is included so we don't need a separate Place Details call
        client.DefaultRequestHeaders.Add("X-Goog-FieldMask",
            "places.displayName,places.location,places.websiteUri,places.primaryTypeDisplayName,places.id");

        foreach (var keyword in SearchKeywords)
        {
            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    textQuery = keyword,
                    // locationRestriction (not locationBias) guarantees results are within the radius
                    locationRestriction = new
                    {
                        circle = new
                        {
                            center = new { latitude = lat, longitude = lng },
                            radius = (double)radiusMeters
                        }
                    },
                    maxResultCount = 20
                });

                var resp = await client.PostAsync(SearchUrl,
                    new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    logger.LogWarning("Google Places (New) returned {Status} for '{Keyword}': {Error}",
                        (int)resp.StatusCode, keyword, err[..Math.Min(200, err.Length)]);
                    continue;
                }

                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("places", out var places)) continue;

                foreach (var place in places.EnumerateArray())
                {
                    var name = place.TryGetProperty("displayName", out var dn)
                        ? dn.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "" : "";
                    if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;

                    var locNode = place.TryGetProperty("location", out var loc) ? loc : (JsonElement?)null;
                    var pLat = locNode?.TryGetProperty("latitude",  out var la) == true ? la.GetDouble() : lat;
                    var pLng = locNode?.TryGetProperty("longitude", out var lo) == true ? lo.GetDouble() : lng;

                    var website  = place.TryGetProperty("websiteUri", out var ws) ? ws.GetString() : null;
                    var industry = place.TryGetProperty("primaryTypeDisplayName", out var pt)
                        ? pt.TryGetProperty("text", out var ptt) ? ptt.GetString() : keyword : keyword;

                    results.Add(new CompanyResult(
                        Name:          name,
                        Website:       website,
                        Industry:      industry,
                        Latitude:      pLat,
                        Longitude:     pLng,
                        DistanceMiles: Haversine(lat, lng, pLat, pLng),
                        SourceProvider: Name));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Google Places search failed for keyword '{Keyword}'", keyword);
            }

            if (results.Count >= 60) break;
        }

        logger.LogInformation("Google Places returned {Count} companies near ({Lat},{Lng})",
            results.Count, lat, lng);

        return results.OrderBy(r => r.DistanceMiles).Take(60).ToList();
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
