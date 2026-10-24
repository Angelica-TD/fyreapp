namespace FyreApp.Services.Uptick.Mapping;

// "Uptick:Mapping" in configuration
public sealed class UptickMappingOptions
{
    // Until cutover, Uptick is the system of record: every Full / Refresh run and Re-map puts fields edited in
    // FyreApp (LocalEdit) back to Uptick's value and closes the edit, whether or not Uptick changed. True keeps
    // FyreApp's value until the field changes in Uptick (docs/uptick-migration.md §7).
    public bool KeepFyreAppEdits { get; set; }
}

// A mapper whose rows can hold fields edited in FyreApp (LocalEdit)
public interface IUptickEditedMapper
{
    // Keep FyreApp's value over Uptick's unchanged one (else Uptick's wins and the edit closes)
    bool KeepFyreAppEdits { get; set; }

    // The Uptick ids of its rows with a FyreApp edit open, mapped again on every run while Uptick wins
    Task<IReadOnlyList<long>> EditedAsync(CancellationToken ct);
}
