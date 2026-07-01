namespace JobRadar.Services.Providers;

public interface ICompanyProvider
{
    string Name        { get; }
    string Description { get; }
    bool   IsAvailable { get; }   // false when a required API key is missing

    Task<List<CompanyResult>> GetCompaniesAsync(double lat, double lng, int radiusMeters);
}

public record CompanyResult(
    string Name,
    string? Website,
    string? Industry,
    double Latitude,
    double Longitude,
    double DistanceMiles,
    string SourceProvider
);
