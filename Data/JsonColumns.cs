using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FyreApp.Data;

// A list of small records kept as one jsonb column (an email's attachments, a dispatch's emails)
public static class JsonColumns
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<List<T>> HasJsonList<T>(this PropertyBuilder<List<T>> property) =>
        property.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                v => JsonSerializer.Serialize(v, Options).GetHashCode(),
                v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Options), Options) ?? new List<T>()));
}
