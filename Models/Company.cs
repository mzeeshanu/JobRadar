namespace JobRadar.Models;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? CareersUrl { get; set; }
    public string? Industry { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? DistanceMiles { get; set; }
    public string? SourceProvider { get; set; }
    public DateTime LastCrawled { get; set; }
    public List<Job> Jobs { get; set; } = new();
}
