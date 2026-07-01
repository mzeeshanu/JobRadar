namespace JobRadar.Models;

public class Job
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Location { get; set; }
    public string? EmploymentType { get; set; }   // Full-time, Part-time, Contract
    public string? ExperienceLevel { get; set; }  // Entry, Mid, Senior, Lead
    public string? Description { get; set; }
    public string? ApplyUrl { get; set; }
    public List<string> Skills { get; set; } = new();
    public DateTime DateFound { get; set; }
    public bool IsRemote { get; set; }
    /// <summary>
    /// False when the job's location is a non-US country even though the company is local.
    /// Shown in the UI with a warning badge rather than being dropped entirely.
    /// </summary>
    public bool IsLocalPosition { get; set; } = true;
}
