namespace JobRadar.Services.Providers;

/// <summary>
/// Marks a class as an auto-discovered place reader (ICompanyProvider).
/// At startup the DI container scans the assembly and registers every class
/// decorated with this attribute — no manual AddScoped call required.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PlaceReaderAttribute(string configKey, string description) : Attribute
{
    /// <summary>Key under Providers:Companies in appsettings, e.g. "OpenStreetMapOverpass".</summary>
    public string ConfigKey   { get; } = configKey;
    public string Description { get; } = description;

    /// <summary>Set to true if this provider needs an API key to function.</summary>
    public bool RequiresApiKey { get; init; } = false;

    /// <summary>Config path for the API key, e.g. "Providers:Companies:GooglePlaces:ApiKey".</summary>
    public string? ApiKeyPath { get; init; }
}

/// <summary>
/// Marks a class as an auto-discovered job provider (IJobProvider).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobReaderAttribute(string configKey, string description) : Attribute
{
    public string ConfigKey   { get; } = configKey;
    public string Description { get; } = description;
    public bool RequiresApiKey { get; init; } = false;
    public string? ApiKeyPath  { get; init; }
}
