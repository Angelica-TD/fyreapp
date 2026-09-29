using FyreApp.Data;
using FyreApp.ViewModels.Imports;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports;

public enum UptickExportType
{
    Properties = 1,
    PropertyContacts = 2,
    Assets = 3,
    Remarks = 4,
    Reports = 5
}

// Collects counts and grouped issues for one import run.
public sealed class ImportContext
{
    private const int MaxIssues = 500;
    private const int MaxRowsPerIssue = 200;

    private readonly Dictionary<string, ImportIssueVm> _issues = new(StringComparer.OrdinalIgnoreCase);

    public ImportContext(UptickImportResultVm result) => Result = result;

    public UptickImportResultVm Result { get; }

    public IEnumerable<ImportIssueVm> Issues => _issues.Values;

    public void Issue(string type, string key, int row, string message)
    {
        var k = $"{type}::{key}";
        if (!_issues.TryGetValue(k, out var issue))
        {
            if (_issues.Count >= MaxIssues) return;
            _issues[k] = issue = new ImportIssueVm { Type = type, Key = key, Message = message };
        }
        if (issue.Rows.Count < MaxRowsPerIssue) issue.Rows.Add(row);
    }

    // Row not imported: counts it under `reason` and records an issue.
    public void Skip(string reason, string key, int row, string message)
    {
        Result.Skipped[reason] = Result.Skipped.GetValueOrDefault(reason) + 1;
        Issue(reason, key, row, message);
    }
}

public abstract class UptickImporter
{
    protected readonly AppDbContext Db;
    protected UptickImporter(AppDbContext db) => Db = db;

    public abstract UptickExportType Type { get; }
    public abstract string DisplayName { get; }

    // Columns that must all be present for a file to be recognised as this export.
    public abstract string[] SignatureHeaders { get; }

    // Build entities from the rows; when not a dry run, save them. Sets Result.Created / SkippedExisting.
    public abstract Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct);

    protected async Task<Dictionary<string, int>> LoadSiteIdsByRefAsync(CancellationToken ct) =>
        (await Db.Sites.AsNoTracking()
            .Where(s => s.ExternalId != null && s.ExternalId != "")
            .Select(s => new { s.Id, s.ExternalId })
            .ToListAsync(ct))
        .ToDictionary(s => s.ExternalId!.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);

    // Returns false (and records why) when the row's external ID is missing, already imported, or repeated in the file.
    protected static bool ClaimExternalId(string? externalId, HashSet<string> existing, HashSet<string> seenInFile,
        ImportRow row, ImportContext ctx)
    {
        if (externalId == null)
        {
            ctx.Skip("Missing ID", "(blank)", row.RowNumber, "Row has no ID. Skipped.");
            return false;
        }

        if (existing.Contains(externalId))
        {
            ctx.Result.SkippedExisting++;
            return false;
        }

        if (!seenInFile.Add(externalId))
        {
            ctx.Skip("Duplicate in file", externalId, row.RowNumber, "ID appears more than once in the file. Later rows skipped.");
            return false;
        }

        return true;
    }
}
