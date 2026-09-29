using FyreApp.ViewModels.Imports;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports;

public interface IUptickImportService
{
    IReadOnlyList<UptickImporter> Importers { get; }

    // type == null auto-detects the export from its column headers.
    Task<UptickImportResultVm> ImportAsync(Stream stream, string fileName, UptickExportType? type, bool dryRun, CancellationToken ct = default);
}

public class UptickImportService : IUptickImportService
{
    // Exports handled by their own pages; recognised so we can point the user there.
    private static readonly (string Name, string[] Headers)[] OtherExports =
    {
        ("Clients", new[] { "Name", "Property Count (Total)", "Primary Contact Name" }),
        ("Routine schedules", new[] { "Routine", "Due Date", "Property ref" })
    };

    public UptickImportService(IEnumerable<UptickImporter> importers) =>
        Importers = importers.OrderBy(i => i.Type).ToList();

    public IReadOnlyList<UptickImporter> Importers { get; }

    public async Task<UptickImportResultVm> ImportAsync(
        Stream stream, string fileName, UptickExportType? type, bool dryRun, CancellationToken ct = default)
    {
        var result = new UptickImportResultVm { DryRun = dryRun, FileName = fileName, Type = type };

        if (!TabularFileReader.IsSupported(fileName))
        {
            result.Error = $"Unsupported file type: {Path.GetExtension(fileName)}. Upload CSV or XLSX.";
            return result;
        }

        List<Dictionary<string, string?>> raw;
        try
        {
            raw = await TabularFileReader.ReadAsync(stream, fileName, ct);
        }
        catch (Exception ex)
        {
            result.Error = $"Could not read file: {ex.Message}";
            return result;
        }

        var headers = raw.FirstOrDefault()?.Keys
            .Select(TabularFileReader.NormalizeHeader)
            .ToHashSet() ?? new HashSet<string>();

        bool Matches(string[] signature) =>
            signature.All(h => headers.Contains(TabularFileReader.NormalizeHeader(h)));

        UptickImporter? importer;
        if (type != null)
        {
            importer = Importers.First(i => i.Type == type);
            if (raw.Count > 0 && !Matches(importer.SignatureHeaders))
            {
                var missing = importer.SignatureHeaders.Where(h => !headers.Contains(TabularFileReader.NormalizeHeader(h)));
                result.Error = $"This doesn't look like an Uptick {importer.DisplayName} export (missing columns: {string.Join(", ", missing)}).";
                return result;
            }
        }
        else
        {
            importer = Importers.FirstOrDefault(i => Matches(i.SignatureHeaders));
            if (importer == null)
            {
                var other = OtherExports.FirstOrDefault(o => Matches(o.Headers)).Name;
                result.Error = other != null
                    ? $"This is an Uptick {other} export; use the {other} import instead."
                    : "Couldn't recognise this export from its columns. Choose the export type and try again.";
                return result;
            }
            result.Type = importer.Type;
        }

        var rows = raw
            .Select((r, i) => new ImportRow(r, i + 1))
            .Where(r => !r.IsBlank)
            .ToList();

        result.TotalRows = rows.Count;
        var ctx = new ImportContext(result);

        try
        {
            await importer.ImportAsync(rows, dryRun, ctx, ct);
        }
        catch (DbUpdateException ex)
        {
            result.Created = 0;
            result.Notes.Clear();
            result.Error = "Database rejected the import; nothing was saved. " + (ex.InnerException?.Message ?? ex.Message);
        }

        result.Issues = ctx.Issues.OrderBy(i => i.Type).ThenBy(i => i.Key).ToList();
        return result;
    }
}
