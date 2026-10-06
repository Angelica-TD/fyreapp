using System.Globalization;
using System.Text;
using CsvHelper;

namespace FyreApp.Infrastructure;

// CSV for the list pages' Download buttons. UTF-8 with BOM so Excel shows names correctly.
public static class CsvExport
{
    public static byte[] Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(true)))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var h in headers) csv.WriteField(h);
            csv.NextRecord();

            foreach (var row in rows)
            {
                foreach (var value in row) csv.WriteField(value);
                csv.NextRecord();
            }
        }
        return ms.ToArray();
    }

    public static string? Date(DateTime? d) => d?.ToString("yyyy-MM-dd");

    public static string FileName(string what) => $"{what}_{DateTime.UtcNow.ToSydney():yyyy-MM-dd_HH-mm}.csv";
}
