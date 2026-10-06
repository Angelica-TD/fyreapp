using System.Text.Encodings.Web;
using System.Text.Json;

namespace FyreApp.Services.Imports;

// The raw Uptick row kept on each imported record (UptickData), so no column is lost.
public static class UptickJson
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Non-blank columns with the export's own headers, in column order; null when the row has none.
    public static string? Serialize(IEnumerable<KeyValuePair<string, string?>> row)
    {
        var values = new Dictionary<string, string>();
        foreach (var (header, value) in row)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values.TryAdd(header, value);
        }
        return values.Count == 0 ? null : JsonSerializer.Serialize(values, Options);
    }

    public static IReadOnlyList<(string Header, string Value)> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .Select(p => (p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString()))
            .ToList();
    }
}
