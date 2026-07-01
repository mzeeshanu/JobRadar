namespace JobRadar.Services;

/// <summary>
/// Filters jobs by location relevance for a US-based user.
/// Rules (in order):
///   1. No location specified → keep (could be local or global remote)
///   2. Explicitly "Remote" with no country → keep
///   3. Location contains a US indicator → keep
///   4. Location is "Remote - [non-US country]" or just names a non-US country → drop
/// </summary>
public static class JobLocationFilter
{
    // Common non-US countries that appear in job listings
    private static readonly HashSet<string> NonUsCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "mexico","canada","uk","united kingdom","england","scotland","wales","ireland",
        "australia","new zealand","india","pakistan","bangladesh","sri lanka",
        "germany","france","spain","italy","netherlands","belgium","switzerland",
        "austria","sweden","norway","denmark","finland","poland","portugal",
        "brazil","argentina","colombia","chile","peru","ecuador","venezuela",
        "china","japan","south korea","korea","singapore","hong kong","taiwan",
        "malaysia","indonesia","philippines","thailand","vietnam",
        "south africa","nigeria","kenya","ghana","egypt","morocco",
        "uae","dubai","saudi arabia","israel","turkey","russia","ukraine",
        "romania","czech republic","hungary","greece","bulgaria","croatia",
        "worldwide","global","international","anywhere","latin america",
        "europe","asia","africa","emea","apac","latam"
    };

    private static readonly HashSet<string> UsIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "united states","usa","us","u.s.","u.s.a.",
        // All 50 state names and abbreviations
        "alabama","al","alaska","ak","arizona","az","arkansas","ar",
        "california","ca","colorado","co","connecticut","ct","delaware","de",
        "florida","fl","georgia","ga","hawaii","hi","idaho","id",
        "illinois","il","indiana","in","iowa","ia","kansas","ks",
        "kentucky","ky","louisiana","la","maine","me","maryland","md",
        "massachusetts","ma","michigan","mi","minnesota","mn","mississippi","ms",
        "missouri","mo","montana","mt","nebraska","ne","nevada","nv",
        "new hampshire","nh","new jersey","nj","new mexico","nm","new york","ny",
        "north carolina","nc","north dakota","nd","ohio","oh","oklahoma","ok",
        "oregon","or","pennsylvania","pa","rhode island","ri","south carolina","sc",
        "south dakota","sd","tennessee","tn","texas","tx","utah","ut",
        "vermont","vt","virginia","va","washington","wa","west virginia","wv",
        "wisconsin","wi","wyoming","wy","district of columbia","dc",
        // Major US cities
        "new york city","los angeles","chicago","houston","phoenix","philadelphia",
        "san antonio","san diego","dallas","san jose","austin","jacksonville",
        "fort worth","columbus","charlotte","san francisco","indianapolis","seattle",
        "denver","boston","nashville","portland","las vegas","memphis","salt lake"
    };

    /// <summary>
    /// Returns true if this job should be shown to a US-based user.
    /// </summary>
    public static bool IsRelevant(string? location, bool isRemote)
    {
        // No location info → keep (we can't rule it out)
        if (string.IsNullOrWhiteSpace(location)) return true;

        var loc = location.Trim();

        // Pure "Remote" with no qualifier → keep
        if (loc.Equals("Remote", StringComparison.OrdinalIgnoreCase)) return true;
        if (loc.Equals("Anywhere", StringComparison.OrdinalIgnoreCase)) return true;

        // Check for US indicators first — "Remote (US)", "Austin, TX", "New York", etc.
        if (ContainsUsIndicator(loc)) return true;

        // "Remote - Mexico", "Mexico City", "Ontario, Canada", etc. → drop
        if (ContainsNonUsCountry(loc)) return false;

        // Location string exists but we can't classify it → keep (benefit of the doubt)
        return true;
    }

    private static bool ContainsUsIndicator(string location)
    {
        // Tokenize on common separators and check each token
        var tokens = location.Split([',', '-', '/', '(', ')', '|', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (UsIndicators.Contains(token)) return true;
        }

        // Also check full string for multi-word indicators
        foreach (var indicator in UsIndicators)
        {
            if (indicator.Contains(' ') &&
                location.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsNonUsCountry(string location)
    {
        var tokens = location.Split([',', '-', '/', '(', ')', '|', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (NonUsCountries.Contains(token)) return true;
        }

        // Check multi-word country names
        foreach (var country in NonUsCountries)
        {
            if (country.Contains(' ') &&
                location.Contains(country, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
