using JobRadar.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace JobRadar.Data;

public class JobRadarDbContext(DbContextOptions<JobRadarDbContext> options) : DbContext(options)
{
    // Original (kept for compatibility)
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Job> Jobs => Set<Job>();

    // Cache layer
    public DbSet<LocationSearch> LocationSearches => Set<LocationSearch>();
    public DbSet<CachedCompany> CachedCompanies => Set<CachedCompany>();
    public DbSet<CachedJob> CachedJobs => Set<CachedJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var listComparer = new ValueComparer<List<string>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            c => c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
            c => c.ToList());

        var jsonOptions = (JsonSerializerOptions?)null;

        modelBuilder.Entity<Job>()
            .Property(j => j.Skills)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(listComparer);

        modelBuilder.Entity<CachedJob>()
            .Property(j => j.Skills)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(listComparer);

        // Index for fast cache lookups by location
        modelBuilder.Entity<LocationSearch>()
            .HasIndex(l => new { l.Lat, l.Lng, l.RadiusMeters });

        modelBuilder.Entity<CachedCompany>()
            .HasIndex(c => c.LocationSearchId);

        modelBuilder.Entity<CachedJob>()
            .HasIndex(j => j.CachedCompanyId);

        modelBuilder.Entity<CachedJob>()
            .HasIndex(j => j.ExpiresAt);
    }
}
