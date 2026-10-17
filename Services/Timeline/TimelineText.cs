using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FyreApp.Services.Timeline;

// Fills in an Uptick timeline event's text the way Uptick shows it. The text is a template:
// "<em>{verbose_name}</em> was changed to <em>{new_val}</em>." with the values in the event's data, and
// "#Dispatch:{dispatch_pk}" naming another record (shown "#24440", its label from data_links as a tooltip).
// The result is safe HTML: everything is encoded except the em, p and small tags Uptick's templates use.
public static partial class TimelineText
{
    [GeneratedRegex(@"#(?<model>\{\w+\}|[A-Za-z]+):(?<pk>\{\w+\}|\d+)|\{(?<name>\w+)\}|<(?<tag>/?(?:em|p|small))>", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    public static string Render(string? template, JsonElement data, JsonElement dataLinks)
    {
        if (string.IsNullOrEmpty(template)) return "";

        var html = new StringBuilder();
        var at = 0;
        foreach (Match m in Token().Matches(template))
        {
            html.Append(Encode(template[at..m.Index]));
            at = m.Index + m.Length;

            if (m.Groups["tag"].Success)
                html.Append('<').Append(m.Groups["tag"].Value.ToLowerInvariant()).Append('>');
            else if (m.Groups["name"].Success)
                html.Append(Encode(Value(data, m.Groups["name"].Value) ?? m.Value));
            else
                html.Append(Link(m, data, dataLinks));
        }
        html.Append(Encode(template[at..]));
        return html.ToString();
    }

    // "#Dispatch:{dispatch_pk}" -> "#24440", with Uptick's label for the record when it gave one
    private static string Link(Match m, JsonElement data, JsonElement dataLinks)
    {
        var model = Part(m.Groups["model"].Value, data);
        var pk = Part(m.Groups["pk"].Value, data);
        if (model == null || pk == null) return Encode(m.Value);

        var label = dataLinks.ValueKind == JsonValueKind.Object
            && dataLinks.TryGetProperty(model, out var links) && links.ValueKind == JsonValueKind.Object
            && links.TryGetProperty(pk, out var link) && link.ValueKind == JsonValueKind.Object
            && link.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
                ? l.GetString()?.Trim()
                : null;
        return string.IsNullOrEmpty(label)
            ? Encode("#" + pk)
            : $"<span title=\"{Encode(label)}\">{Encode("#" + pk)}</span>";
    }

    // A link's model or pk: written out, or a placeholder for a value in the data
    private static string? Part(string part, JsonElement data) =>
        part.StartsWith('{') ? Value(data, part[1..^1]) : part;

    // A value as Uptick (Python) writes it: None, True, False, numbers as they are
    private static string? Value(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => "None",
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => v.GetRawText()
        };
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
