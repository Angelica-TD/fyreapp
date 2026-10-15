using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FyreApp.Services.Uptick.Api;

// One JSON:API record from Uptick, with its attributes and relationships kept as the raw JSON received
public sealed record UptickResource(
    string Type,
    string Id,
    string Attributes,
    string Relationships,
    DateTime? CreatedUtc,
    DateTime? UpdatedUtc)
{
    // Changes when anything in the record does
    public string Hash { get; } = HashOf(Attributes, Relationships);

    private static string HashOf(string attributes, string relationships) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(attributes + "\n" + relationships)));

    public static UptickResource FromJson(JsonElement e)
    {
        // A few endpoints (e.g. intelligencereports/profitability_by_task) return plain rows under "results"
        // instead of JSON:API records: the whole row is the attributes, keyed by "id" or else its first "..._id"
        if (!e.TryGetProperty("attributes", out _) && !e.TryGetProperty("relationships", out _))
        {
            var key = e.TryGetProperty("id", out var plainId)
                ? plainId
                : e.EnumerateObject().FirstOrDefault(p => p.Name.EndsWith("_id", StringComparison.Ordinal)).Value;
            if (key.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw new UptickApiException(0, "An Uptick record has no id.");
            return new UptickResource("", key.ToString(), e.GetRawText(), "{}", TimeOf(e, "created"), TimeOf(e, "updated"));
        }

        var attributes = e.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        return new UptickResource(
            e.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
            e.GetProperty("id").ToString(),
            attributes.ValueKind == JsonValueKind.Object ? attributes.GetRawText() : "{}",
            e.TryGetProperty("relationships", out var r) && r.ValueKind == JsonValueKind.Object ? r.GetRawText() : "{}",
            TimeOf(attributes, "created"),
            TimeOf(attributes, "updated"));
    }

    private static DateTime? TimeOf(JsonElement attributes, string name) =>
        attributes.ValueKind == JsonValueKind.Object
        && attributes.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(v.GetString(), out var dto)
            ? dto.UtcDateTime
            : null;
}

// One page of an endpoint's list: its records, and the total Uptick reports for the query
public sealed record UptickPage(int? Count, IReadOnlyList<UptickResource> Records);

// Uptick answered with an error status (403 = the API login can't see this endpoint)
public class UptickApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

// Uptick asked FyreApp to wait 30 minutes or more: stop rather than keep knocking (as Airbyte's connector does)
public class UptickRateLimitedException(TimeSpan retryAfter)
    : Exception($"Uptick asked to wait {retryAfter.TotalMinutes:0} minutes before the next request. The run stopped; try again later.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
