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
                dict[headers[i]] = row.Cell(i + 1).GetValue<string>();

            rows.Add(dict);
        }

        return rows;
    }
}
