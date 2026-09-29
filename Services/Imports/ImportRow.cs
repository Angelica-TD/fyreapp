using System.Globalization;

namespace FyreApp.Services.Imports;

// One data row from an export, with header lookups that ignore case, spacing and punctuation.
public sealed class ImportRow
{
    private readonly Dictionary<string, string?> _values;

    public int RowNumber { get; }

    public ImportRow(IReadOnlyDictionary<string, string?> raw, int rowNumber)
    {
        RowNumber = rowNumber;
        _values = new Dictionary<string, string?>();
        foreach (var (header, value) in raw)
            _values.TryAdd(TabularFileReader.NormalizeHeader(header), value);
    }

    public bool IsBlank => _values.Values.All(string.IsNullOrWhiteSpace);

    // First non-blank value among the given headers, trimmed.
    public string? Get(params string[] headers)
    {
        foreach (var h in headers)
        {
            if (_values.TryGetValue(TabularFileReader.NormalizeHeader(h), out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }

    public bool? GetBool(string header)
    {
        var v = Get(header)?.ToLowerInvariant();
        return v switch
        {
            "true" or "t" or "yes" or "y" or "1" => true,
            "false" or "f" or "no" or "n" or "0" => false,
            _ => null
        };
    }

    public int? GetInt(string header) =>
        int.TryParse(Get(header), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    // Calendar date (no time) stored as UTC midnight, matching how schedule due dates are stored.
    public DateTime? GetDate(string header)
    {
        var v = Get(header);
        if (v == null) return null;

        var formats = new[] { "yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd HH:mm:ss", "d/M/yyyy H:mm" };
        if (DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);

        return null;
    }

    // Point in time. Uptick exports local (Queensland) time unless the value carries an offset.
    public DateTime? GetTimestampUtc(string header) => UptickTime.ParseToUtc(Get(header));
}
