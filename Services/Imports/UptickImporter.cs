using FyreApp.Data;
using FyreApp.Models;
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

    // Runs the import in one transaction, so a failure saves nothing (placeholders included).
    public async Task RunAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        if (dryRun || !Db.Database.IsRelational())
        {
            await ImportAsync(rows, dryRun, ctx, ct);
            return;
        }

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        await ImportAsync(rows, dryRun, ctx, ct);
        await tx.CommitAsync(ct);
    }

    protected async Task<Dictionary<string, int>> LoadSiteIdsByRefAsync(CancellationToken ct) =>
        (await Db.Sites.AsNoTracking()
            .Where(s => s.ExternalId != null && s.ExternalId != "")
            .Select(s => new { s.Id, s.ExternalId })
            .ToListAsync(ct))
        .ToDictionary(s => s.ExternalId!.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);

    public const string UnassignedClientName = "Unassigned (not in Uptick export)";

    // Holds placeholder properties whose client isn't known from the row
    protected async Task<Client> GetUnassignedClientAsync(CancellationToken ct) =>
        await Db.Clients.FirstOrDefaultAsync(c => c.IsPlaceholder && c.ExternalId == null && c.Name == UnassignedClientName, ct)
        ?? new Client { Name = UnassignedClientName, IsPlaceholder = true, Active = false, Created = DateTime.UtcNow };

    // A row can point at a property that isn't in FyreApp, usually because it's archived in Uptick and so
    // missing from the properties export. Rather than skip the row, create an inactive placeholder property
    // (ref + name from the row) and add it to sitesByRef. Importing a properties export that includes it
    // later fills the placeholder in. Dry runs only count them (with temporary negative ids).
    protected async Task AddPlaceholderSitesAsync(IEnumerable<ImportRow> rows, Dictionary<string, int> sitesByRef,
        string propertyNameHeader, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var missing = new Dictionary<string, ImportRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var propertyRef = row.Get("Property Ref");
            if (propertyRef != null && !sitesByRef.ContainsKey(propertyRef))
                missing.TryAdd(propertyRef, row);
        }
        if (missing.Count == 0) return;

        // Some exports say which client the property belongs to; use it when exactly one client has that name
        var clientIdByName = (await Db.Clients.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);

        Client? unassigned = null;
        var placeholders = new List<(string Ref, Site Site)>();

        foreach (var (propertyRef, row) in missing)
        {
            var site = new Site
            {
                ExternalId = propertyRef,
                Name = row.Get(propertyNameHeader) ?? propertyRef,
                Active = false,
                IsPlaceholder = true
            };

            var clientName = row.Get("Client");
            if (clientName != null && clientIdByName.TryGetValue(clientName, out var clientId))
                site.ClientId = clientId;
            else
                site.Client = unassigned ??= await GetUnassignedClientAsync(ct);

            placeholders.Add((propertyRef, site));
            ctx.Issue("Placeholder property", propertyRef, row.RowNumber,
                $"Property '{site.Name}' isn't in FyreApp (probably archived in Uptick, so not in the properties export). " +
                "Created as an inactive placeholder; import a properties export that includes it to fill it in.");
        }

        ctx.Result.Notes.Add(
            $"{placeholders.Count} propert{(placeholders.Count == 1 ? "y" : "ies")} referenced here {(placeholders.Count == 1 ? "isn't" : "aren't")} in FyreApp " +
            $"and {(dryRun ? "will be" : "were")} created as inactive placeholders. Import a properties export that includes archived properties to fill them in.");

        if (dryRun)
        {
            var tempId = -1;
            foreach (var (propertyRef, _) in placeholders) sitesByRef[propertyRef] = tempId--;
            return;
        }

        Db.Sites.AddRange(placeholders.Select(p => p.Site));
        await Db.SaveChangesAsync(ct);
        foreach (var (propertyRef, site) in placeholders) sitesByRef[propertyRef] = site.Id;
    }

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
