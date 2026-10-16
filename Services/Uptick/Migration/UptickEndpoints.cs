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

    // Endpoints whose pages aren't in a stable order, so offset paging skips records whatever is tried: they're
    // fetched again one parent at a time, for the parents whose records held don't add up (§4.5). Copied after
    // the parent endpoint, which says what they should add up to.
    // invoicelineitems: sorted by "index" (0 on almost every line), so ~15k of ~23k come back
    internal static readonly Dictionary<string, PerParent> FilledPerParent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["invoicelineitems"] = new("invoices", "invoice", "subtotal", "subtotal")
    };

    // Parent endpoint, the child's filter and relationship naming it, and the amounts children add up to
    internal sealed record PerParent(string Parent, string Filter, string ParentAmount, string ChildAmount);

    public static bool IsExcluded(string resource) => Excluded.ContainsKey(resource);

    public static string? WhyExcluded(string resource) => Excluded.GetValueOrDefault(resource);
}
