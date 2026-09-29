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
        Stream stream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".xlsx" ? ReadExcel(stream) : await ReadCsvAsync(stream, ct);
    }

    public static string NormalizeHeader(string s) =>
        new string(s.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static async Task<List<Dictionary<string, string?>>> ReadCsvAsync(Stream stream, CancellationToken ct)
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

        if (!await csv.ReadAsync()) return rows;
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                dict[h] = csv.GetField(h);

            rows.Add(dict);
        }

        return rows;
    }

    private static List<Dictionary<string, string?>> ReadExcel(Stream stream)
    {
        var rows = new List<Dictionary<string, string?>>();

        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        var headerRow = ws.FirstRowUsed();
        if (headerRow == null) return rows;

        var headers = headerRow.CellsUsed().Select(c => c.GetString()).ToList();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
                dict[headers[i]] = CellText(row.Cell(i + 1));

            rows.Add(dict);
        }

        return rows;
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
