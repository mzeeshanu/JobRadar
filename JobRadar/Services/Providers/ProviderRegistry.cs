using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.Services.Providers;

/// <summary>
/// Scans the assembly for all classes decorated with [PlaceReader] or [JobReader]
/// and registers them as ICompanyProvider / IJobProvider automatically.
///
/// Adding a new provider = create the class + add the attribute. No Program.cs changes needed.
/// </summary>
public static class ProviderRegistry
{
    public static void AddDiscoveredProviders(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var types    = assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract).ToList();

        // ── Company providers (ICompanyProvider + [PlaceReader]) ──────────────
        foreach (var type in types.Where(t =>
            t.GetCustomAttribute<PlaceReaderAttribute>() != null &&
            typeof(ICompanyProvider).IsAssignableFrom(t)))
        {
            services.AddScoped(typeof(ICompanyProvider), type);
        }

        // ── Job providers (IJobProvider + [JobReader]) ────────────────────────
        foreach (var type in types.Where(t =>
            t.GetCustomAttribute<JobReaderAttribute>() != null &&
            typeof(IJobProvider).IsAssignableFrom(t)))
        {
            services.AddScoped(typeof(IJobProvider), type);
        }
    }

    /// <summary>Returns metadata for all discovered providers — used by the UI settings panel.</summary>
    public static List<ProviderMeta> GetAllProviderMeta()
    {
        var list     = new List<ProviderMeta>();
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            var pr = type.GetCustomAttribute<PlaceReaderAttribute>();
            if (pr != null)
                list.Add(new ProviderMeta(pr.ConfigKey, pr.Description, "Companies",
                    pr.RequiresApiKey, pr.ApiKeyPath, type.Name));

            var jr = type.GetCustomAttribute<JobReaderAttribute>();
            if (jr != null)
                list.Add(new ProviderMeta(jr.ConfigKey, jr.Description, "Jobs",
                    jr.RequiresApiKey, jr.ApiKeyPath, type.Name));
        }

        return list;
    }
}

/// <summary>Static metadata about a provider — for settings/status display.</summary>
public record ProviderMeta(
    string ConfigKey,
    string Description,
    string Category,       // "Companies" | "Jobs"
    bool   RequiresApiKey,
    string? ApiKeyPath,
    string TypeName
);
