namespace JobRadar.Models;

public class LocationSearch
{
    public int Id { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RadiusMeters { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastRefreshedAt { get; set; }
    public List<CachedCompany> Companies { get; set; } = [];
}
