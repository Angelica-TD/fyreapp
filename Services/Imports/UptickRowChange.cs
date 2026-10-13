namespace FyreApp.Services.Imports;

// Re-importing an export updates records that changed in Uptick since they were imported. Each imported
// record keeps the Uptick row it came from (UptickData), so a row that differs from it has changed.
public static class UptickRowChange
{
    // True when the export row should overwrite the record: it differs from the row last imported, and isn't
    // older by Uptick's "Updated" time (an old export imported after a newer one leaves the record alone).
    // Rows with the same "Updated" but different values still update: some columns are worked out by Uptick
    // from other records (e.g. an asset's last service date) without touching "Updated".
    public static bool ShouldUpdate(string? rowJson, string? storedJson)
    {
        if (string.Equals(SameLineBreaks(rowJson), SameLineBreaks(storedJson), StringComparison.Ordinal)) return false;

        return !(UpdatedUtc(rowJson) is DateTime row && UpdatedUtc(storedJson) is DateTime stored && row < stored);
    }

    // Multi-line values (notes, addresses) come with Windows or Unix line breaks depending on what last saved
    // the file (e.g. opened and saved in Excel); that alone isn't a change. JSON escapes them as \r\n / \n.
    private static string? SameLineBreaks(string? json) => json?.Replace("\\r\\n", "\\n").Replace("\\r", "\\n");

    private static DateTime? UpdatedUtc(string? json)
    {
        foreach (var (header, value) in UptickJson.Parse(json))
        {
            if (string.Equals(header.Trim(), "Updated", StringComparison.OrdinalIgnoreCase))
                return UptickTime.ParseToUtc(value);
        }
        return null;
    }
}
