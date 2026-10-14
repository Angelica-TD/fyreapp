using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace FyreApp.Services.Imports;

// Reads CSV/TSV/XLSX exports into header -> value rows (headers matched case-insensitively).
public static class TabularFileReader
{
    public static bool IsSupported(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".csv" or ".txt" or ".xlsx";

    public static async Task<List<Dictionary<string, string?>>> ReadAsync(
        Stream stream, string fileName, CancellationToken ct = default) =>
        (await ReadTableAsync(stream, fileName, ct)).Rows;

    // The header row as well as the rows, so an export with no rows (e.g. an empty prompt questions
    // export) can still be recognised from its columns
    public sealed record Table(IReadOnlyList<string> Headers, List<Dictionary<string, string?>> Rows);

    public static async Task<Table> ReadTableAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".xlsx" ? ReadExcel(stream) : await ReadCsvAsync(stream, ct);
    }

    public static string NormalizeHeader(string s) =>
        new string(s.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static async Task<Table> ReadCsvAsync(Stream stream, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, string?>>();

        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = new[] { ",", "\t" },
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync()) return new Table([], rows);
        csv.ReadHeader();
        var headers = UniqueHeaders(csv.HeaderRecord ?? Array.Empty<string>());

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
                dict[headers[i]] = csv.GetField(i);

            rows.Add(dict);
        }

        return new Table(headers, rows);
    }

    private static Table ReadExcel(Stream stream)
    {
        var rows = new List<Dictionary<string, string?>>();

        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        var headerRow = ws.FirstRowUsed();
        if (headerRow == null) return new Table([], rows);

        var headers = UniqueHeaders(headerRow.CellsUsed().Select(c => c.GetString()).ToList());

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
                dict[headers[i]] = CellText(row.Cell(i + 1));

            rows.Add(dict);
        }

        return new Table(headers, rows);
    }

    // A header used twice keeps both columns: the second becomes e.g. "License (2)". (Uptick's users export
    // has two "License" columns: the licence type, then the trade licence number.)
    internal static List<string> UniqueHeaders(IReadOnlyList<string> headers)
    {
        var seen = new Dictionary<string, int>();
        var unique = new List<string>(headers.Count);
        foreach (var h in headers)
        {
            var key = NormalizeHeader(h);
            seen[key] = seen.GetValueOrDefault(key) + 1;
            unique.Add(seen[key] == 1 ? h : $"{h} ({seen[key]})");
        }
        return unique;
    }

    // Date cells as ISO text so parsing doesn't depend on the server's culture
    // (GetValue<string>() would give e.g. "1/02/2023 12:00:00 AM" on an en-AU machine).
    private static string CellText(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt))
            return dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return cell.GetValue<string>();
    }
}
