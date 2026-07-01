namespace JobRadar.Models;

public class CachedJob
{
    public int Id { get; set; }
    public int CachedCompanyId { get; set; }
    public CachedCompany CachedCompany { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Location { get; set; }
    public string? EmploymentType { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? Description { get; set; }
    public string? ApplyUrl { get; set; }
    public bool IsRemote { get; set; }
    public bool IsLocalPosition { get; set; } = true;
    public List<string> Skills { get; set; } = [];
    public string? SourceProvider { get; set; }  // greenhouse | lever | workday | playwright | html
    public DateTime DateFound { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Computed in C# only — use ExpiresAt < DateTime.UtcNow in EF queries
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}
