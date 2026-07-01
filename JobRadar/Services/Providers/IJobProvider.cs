namespace JobRadar.Services.Providers;

public interface IJobProvider
{
    string Name        { get; }
    string Description { get; }
    bool   IsAvailable { get; }   // false when a required API key is missing

    Task<List<JobResult>> GetJobsAsync(double lat, double lng, int radiusMiles, string? keywords = null);
}

public record JobResult(
    string Title,
    string CompanyName,
    string? Location,
    string? Description,
    string? ApplyUrl,
    string? ExperienceLevel,
    bool IsRemote,
    List<string> Skills,
    string SourceProvider
);
