namespace FyreApp.Services.Uptick.Migration;

// Which of the endpoints Uptick's API lists the migration copies (docs/uptick-migration.md §4.2). Everything is
// copied unless it's listed here, so an endpoint Uptick adds later is picked up without a code change.
public static class UptickEndpoints
{
    // About Uptick itself or its users' sessions, not Fyrepower's data
    private static readonly Dictionary<string, string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oauth-applications"] = "Uptick's API clients",
        ["pushnotificationtokens"] = "Uptick app push notification tokens",
        ["userpreferences"] = "Uptick screen preferences",
        ["schedulerpresets"] = "Uptick scheduler screen presets",
        ["schedulerfavouritecontexts"] = "Uptick scheduler screen favourites",
        ["mods"] = "Uptick add-ons",
        ["extensions"] = "Uptick add-ons",
        ["onboarding-actions"] = "Uptick onboarding checklist",
        ["chatbotsessions"] = "Uptick assistant sessions",
        ["competitormigration"] = "Uptick's own migration tool",
        ["logins"] = "Uptick login sessions",
        ["constance"] = "Uptick system settings",
        ["notifications"] = "Uptick in-app notifications",
        // Computed views over other endpoints, copied already
        ["annotatedroutines"] = "Same records as routines, with computed fields",
        ["annotatedstock"] = "Same records as stock, with computed fields",
        ["properties-with-riskareas"] = "Same records as properties, with risk areas"
    };

    public static bool IsExcluded(string resource) => Excluded.ContainsKey(resource);

    public static string? WhyExcluded(string resource) => Excluded.GetValueOrDefault(resource);
}
