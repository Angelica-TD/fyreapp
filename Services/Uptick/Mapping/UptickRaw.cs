using System.Globalization;
using System.Text.Json;
using FyreApp.Models;

namespace FyreApp.Services.Uptick.Mapping;

// One raw record read for mapping: its numeric Uptick id, attributes and relationships
public sealed class UptickRaw
{
    public UptickRecord Record { get; }
    public long Id { get; }
    public JsonElement A { get; }
    public JsonElement R { get; }

    private UptickRaw(UptickRecord record, long id, JsonElement attributes, JsonElement relationships)
    {
        Record = record;
        Id = id;
        A = attributes;
        R = relationships;
    }

    // Gone from Uptick: the raw copy marks it deleted
    public bool Deleted => Record.DeletedUtc != null;

    public static UptickRaw Parse(UptickRecord record, long id)
    {
        using var a = JsonDocument.Parse(record.Attributes);
        using var r = JsonDocument.Parse(record.Relationships);
        return new UptickRaw(record, id, a.RootElement.Clone(), r.RootElement.Clone());
    }

    // An attribute as text: trimmed, null when blank, cut to the column's length
    public string? Text(string name, int? max = null)
    {
        var s = Str(A, name)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return max != null && s.Length > max ? s[..max.Value] : s;
    }

    public bool? Bool(string name) => Get(A, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null
    };

    public decimal? Decimal(string name) => Get(A, name) switch
    {
        { ValueKind: JsonValueKind.Number } v when v.TryGetDecimal(out var n) => n,
        { ValueKind: JsonValueKind.String } v when decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n,
        _ => null
    };

    // Uptick's times carry their offset ("+11:00"); stored in UTC
    public DateTime? Time(string name) =>
        Str(A, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.UtcDateTime
            : null;

    // The Uptick id a relationship points to ("client": {"data": {"id": "2806", ...}}), null when unset
    public long? Rel(string name) =>
        Get(R, name) is { ValueKind: JsonValueKind.Object } rel && rel.TryGetProperty("data", out var d)
            && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var id)
            && long.TryParse(id.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;

    public static JsonElement Get(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) ? v : default;

    public static string? Str(JsonElement o, string name) => Get(o, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static long? Long(JsonElement o, string name) => Get(o, name) switch
    {
        { ValueKind: JsonValueKind.Number } v when v.TryGetInt64(out var n) => n,
        { ValueKind: JsonValueKind.String } v when long.TryParse(v.GetString(), out var n) => n,
        _ => null
    };

    public static double? Double(JsonElement o, string name) => Get(o, name) switch
    {
        { ValueKind: JsonValueKind.Number } v => v.GetDouble(),
        { ValueKind: JsonValueKind.String } v when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        _ => null
    };
}

// A record that can't be mapped yet, e.g. its client isn't in FyreApp; it stays waiting
public class UptickMapException(string message) : Exception(message);
