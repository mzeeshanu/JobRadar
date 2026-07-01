namespace JobRadar.Services.Providers;

/// <summary>
/// Runtime view of every provider: is it enabled in config, is it available (key present), etc.
/// Injected into pages/components that want to show a provider status panel.
/// </summary>
public class ProviderStatusService(
    IEnumerable<ICompanyProvider> companyProviders,
    IEnumerable<IJobProvider>     jobProviders,
    IConfiguration config)
{
    public List<ProviderStatus> GetAll()
    {
        var list = new List<ProviderStatus>();

        foreach (var p in companyProviders)
        {
            var meta    = GetMeta(p.GetType());
            var enabled = IsEnabled("Companies", meta?.ConfigKey ?? p.Name);
            list.Add(new ProviderStatus(
                Name:          p.Name,
                Description:   p.Description,
                Category:      "Companies",
                Enabled:       enabled,
                Available:     p.IsAvailable,
                RequiresKey:   meta?.RequiresApiKey ?? false,
                MissingKey:    (meta?.RequiresApiKey ?? false) && !p.IsAvailable,
                ConfigKey:     meta?.ConfigKey ?? p.Name));
        }

        foreach (var p in jobProviders)
        {
            var meta    = GetMeta(p.GetType());
            var enabled = IsEnabled("Jobs", meta?.ConfigKey ?? p.Name);
            list.Add(new ProviderStatus(
                Name:          p.Name,
                Description:   p.Description,
                Category:      "Jobs",
                Enabled:       enabled,
                Available:     p.IsAvailable,
                RequiresKey:   meta?.RequiresApiKey ?? false,
                MissingKey:    (meta?.RequiresApiKey ?? false) && !p.IsAvailable,
                ConfigKey:     meta?.ConfigKey ?? p.Name));
        }

        return list;
    }

    public List<ProviderStatus> GetCompanyProviders() =>
        GetAll().Where(p => p.Category == "Companies").ToList();

    public List<ProviderStatus> GetJobProviders() =>
        GetAll().Where(p => p.Category == "Jobs").ToList();

    private bool IsEnabled(string category, string configKey)
    {
        var key = $"Providers:{category}:{configKey}:Enabled";
        var val = config[key];
        return val == null || bool.TrueString.Equals(val, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderMeta? GetMeta(Type type) =>
        ProviderRegistry.GetAllProviderMeta()
            .FirstOrDefault(m => m.TypeName == type.Name);
}

public record ProviderStatus(
    string Name,
    string Description,
    string Category,
    bool   Enabled,
    bool   Available,
    bool   RequiresKey,
    bool   MissingKey,
    string ConfigKey
);
