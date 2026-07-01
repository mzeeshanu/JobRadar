using JobRadar.Models;
using System.Text.Json;

namespace JobRadar.Services;

public class CompanyDiscoveryService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<CompanyDiscoveryService> logger)
{
    private readonly string _apiKey = config["GooglePlaces:ApiKey"] ?? string.Empty;

    public async Task<List<Company>> FindNearbyCompaniesAsync(double lat, double lng, int radiusMeters = 20000)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogWarning("Google Places API key not configured — returning demo companies.");
            return GetDemoCompanies(lat, lng);
        }

        var companies = new List<Company>();
        var client = httpFactory.CreateClient();

        // Search for businesses (offices / tech companies) nearby
        var types = new[] { "software_company", "it_company", "financial_services", "hospital", "university" };

        foreach (var type in types)
        {
            var url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
                      $"?location={lat},{lng}&radius={radiusMeters}&type=establishment" +
                      $"&keyword={type}&key={_apiKey}";

            try
            {
                var resp = await client.GetAsync(url);
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var results = doc.RootElement.GetProperty("results");

                foreach (var place in results.EnumerateArray())
                {
                    var name = place.GetProperty("name").GetString() ?? "";
                    if (companies.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    double placeLat = 0, placeLng = 0;
                    if (place.TryGetProperty("geometry", out var geo) &&
                        geo.TryGetProperty("location", out var loc))
                    {
                        placeLat = loc.GetProperty("lat").GetDouble();
                        placeLng = loc.GetProperty("lng").GetDouble();
                    }

                    companies.Add(new Company
                    {
                        Name = name,
                        Industry = type.Replace("_", " "),
                        Latitude = placeLat,
                        Longitude = placeLng,
                        DistanceMiles = CalculateDistance(lat, lng, placeLat, placeLng)
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google Places call failed for type {Type}", type);
            }

            if (companies.Count >= 30) break;
        }

        return companies.OrderBy(c => c.DistanceMiles).Take(30).ToList();
    }

    private static List<Company> GetDemoCompanies(double lat, double lng) =>
    [
        new() { Name = "Acme Technologies", Website = "https://acme.example.com", Industry = "Technology", Latitude = lat + 0.02, Longitude = lng + 0.01, DistanceMiles = 1.4 },
        new() { Name = "Bright Financial", Website = "https://bright.example.com", Industry = "Finance", Latitude = lat - 0.01, Longitude = lng + 0.02, DistanceMiles = 2.1 },
        new() { Name = "NovaSoft Inc.", Website = "https://novasoft.example.com", Industry = "Software", Latitude = lat + 0.03, Longitude = lng - 0.01, DistanceMiles = 2.8 },
        new() { Name = "HealthBridge Corp", Website = "https://healthbridge.example.com", Industry = "Healthcare", Latitude = lat - 0.02, Longitude = lng - 0.02, DistanceMiles = 3.5 },
        new() { Name = "CloudNine Systems", Website = "https://cloudnine.example.com", Industry = "Cloud Services", Latitude = lat + 0.04, Longitude = lng + 0.03, DistanceMiles = 4.2 },
        new() { Name = "DataStream Analytics", Website = "https://datastream.example.com", Industry = "Data", Latitude = lat - 0.03, Longitude = lng + 0.04, DistanceMiles = 5.0 },
    ];

    private static double CalculateDistance(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3959; // Earth radius in miles
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
