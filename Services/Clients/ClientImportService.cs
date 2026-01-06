using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using FyreApp.Data;
using FyreApp.Dtos;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Clients;

public class ClientImportService : IClientImportService
{
    private readonly AppDbContext _db;
    public ClientImportService(AppDbContext db) => _db = db;

    private sealed record Candidate(Client Client, int PropertyCountTotal, int RowNumber);

    public async Task<ClientImportResultDto> ImportAsync(
        IFormFile file,
        bool dryRun,
        Action<ClientImportProgressDto>? reportProgress = null,
        string? importId = null,
        CancellationToken ct = default)
    {
        var result = new ClientImportResultDto();

        if (file == null || file.Length == 0)
        {
            result.Failed++;
            result.Messages.Add("No file uploaded.");

            reportProgress?.Invoke(new ClientImportProgressDto
            {
                ImportId = importId ?? "",
                DryRun = dryRun,
                Completed = true,
                Failed = 1,
                Message = "No file uploaded."
            });

            return result;
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var totalRows = await EstimateTotalRowsAsync(file, ext, ct);

        const int MaxReportMessages = 50;
        const int MaxIssues = 500;
        const int MaxRowsPerIssue = 500;
        const int MaxIdValuesPerIssue = 500;

        // Group issues by (Type + Key) so we can aggregate row numbers + ID values
        var issuesByKey = new Dictionary<string, ClientImportIssueDto>(StringComparer.OrdinalIgnoreCase);

        void AddIssue(string type, string key, IEnumerable<int> rows, string message, IEnumerable<string>? idValues)
        {
            var issueKey = $"{type}::{key}";
            if (!issuesByKey.TryGetValue(issueKey, out var issue))
            {
                if (issuesByKey.Count >= MaxIssues) return;

                issue = new ClientImportIssueDto
                {
                    Type = type,
                    Key = key,
                    Message = message
                };
                issuesByKey[issueKey] = issue;
            }

            foreach (var r in rows)
            {
                if (issue.Rows.Count >= MaxRowsPerIssue) break;
                if (!issue.Rows.Contains(r))
                    issue.Rows.Add(r);
            }

            if (idValues != null)
            {
                foreach (var raw in idValues)
                {
                    if (issue.IdValues.Count >= MaxIdValuesPerIssue) break;
                    var v = (raw ?? "").Trim();
                    if (v.Length == 0) continue;
                    if (!issue.IdValues.Contains(v))
                        issue.IdValues.Add(v);
                }
            }

            issue.Rows.Sort();
            issue.IdValues.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var progress = new ClientImportProgressDto
        {
            ImportId = importId ?? "",
            DryRun = dryRun,
            Total = totalRows,
            Processed = 0,
            Message = dryRun ? "Dry-run started..." : "Import started..."
        };

        void Push(string? message = null, bool completed = false)
        {
            if (message != null) progress.Message = message;
            progress.Completed = completed;

            progress.WouldCreate = result.Created;
            progress.SkippedDuplicateExternalId = result.SkippedDuplicateExternalId;
            progress.SkippedDuplicateName = result.SkippedDuplicateName;
            progress.SkippedMissingName = result.SkippedMissingName;
            // progress.SkippedDuplicateExternalIdInFile = result.SkippedDuplicateExternalIdInFile;
            // progress.SkippedDuplicateExternalIdMultipleHighPropertyCount = result.SkippedDuplicateExternalIdMultipleHighPropertyCount;
            progress.SkippedInvalid = result.SkippedInvalid;

            progress.Failed = result.Failed;

            progress.ReportMessages = result.Messages.Take(MaxReportMessages).ToList();
            progress.Issues = issuesByKey.Values.ToList();

            reportProgress?.Invoke(progress);
        }

        // DB "already exists" lookups (case-insensitive)
        var existingExternalIdsDb = new HashSet<string>(
            await _db.Clients.AsNoTracking()
                .Where(c => c.ExternalId != null && c.ExternalId != "")
                .Select(c => c.ExternalId!)
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var dbNames = new HashSet<string>(
            await _db.Clients.AsNoTracking()
                .Select(c => c.Name)
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        // "Selected" within file (so we don't import duplicates inside the file)
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedByExternalId = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        var conflictedExternalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Push();

            if (ext is ".csv" or ".txt")
            {
                await foreach (var row in ReadCsvRows(file, ct))
                {
                    result.TotalRows++;

                    ProcessRow(
                        row,
                        rowNumber: result.TotalRows,
                        existingExternalIdsDb: existingExternalIdsDb,
                        dbNames: dbNames,
                        selectedNames: selectedNames,
                        selectedByExternalId: selectedByExternalId,
                        conflictedExternalIds: conflictedExternalIds,
                        result: result,
                        addIssue: AddIssue);

                    progress.Processed++;
                    result.Created = selectedByExternalId.Count;

                    if (progress.Processed % 25 == 0)
                        Push();
                }
            }
            else if (ext == ".xlsx")
            {
                foreach (var row in ReadExcelRows(file))
                {
                    result.TotalRows++;

                    ProcessRow(
                        row,
                        rowNumber: result.TotalRows,
                        existingExternalIdsDb: existingExternalIdsDb,
                        dbNames: dbNames,
                        selectedNames: selectedNames,
                        selectedByExternalId: selectedByExternalId,
                        conflictedExternalIds: conflictedExternalIds,
                        result: result,
                        addIssue: AddIssue);

                    progress.Processed++;
                    result.Created = selectedByExternalId.Count;

                    if (progress.Processed % 25 == 0)
                        Push();
                }
            }
            else
            {
                result.Failed++;
                result.Messages.Add($"Unsupported file type: {ext}. Upload CSV or XLSX.");
                Push($"Unsupported file type: {ext}.", completed: true);
                return result;
            }

            // Final create list
            var toCreate = selectedByExternalId.Values
                .Select(v => v.Client)
                .ToList();

            // In dry-run, this is "would create"
            result.Created = toCreate.Count;

            if (dryRun)
            {
                Push("Dry-run complete (no data was saved).", completed: true);
                return result;
            }

            if (toCreate.Count == 0)
            {
                Push("Nothing to import.", completed: true);
                return result;
            }

            // ---------------------------------------------
            // IMPORTANT: Postgres-friendly insert:
            // INSERT ... ON CONFLICT DO NOTHING
            // Skips only duplicates/constraint conflicts.
            // ---------------------------------------------
            var inserted = await InsertClientsIgnoreConflictsAsync(toCreate, ct);
            var skippedByDb = toCreate.Count - inserted;

            result.Created = inserted;

            if (skippedByDb > 0)
            {
                if (result.Messages.Count < MaxReportMessages)
                    result.Messages.Add($"Skipped {skippedByDb} row(s) due to database uniqueness constraints (Name/ExternalId).");

                Push($"Import complete with warnings. Inserted {inserted}. Skipped {skippedByDb} due to database constraints.", completed: true);
            }
            else
            {
                Push("Import complete.", completed: true);
            }

            return result;
        }
        catch (DbUpdateException ex)
        {
            result.Failed++;
            result.Messages.Add("Database rejected some rows.");
            result.Messages.Add(ex.InnerException?.Message ?? ex.Message);

            AddIssue(
                type: "DatabaseError",
                key: "DbUpdateException",
                rows: Array.Empty<int>(),
                message: ex.InnerException?.Message ?? ex.Message,
                idValues: null);

            Push("Import failed: database error.", completed: true);
            return result;
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.Messages.Add(ex.Message);

            AddIssue(
                type: "UnhandledError",
                key: "Exception",
                rows: Array.Empty<int>(),
                message: ex.Message,
                idValues: null);

            Push($"Import failed: {ex.Message}", completed: true);
            return result;
        }
    }

    private static void ProcessRow(
    Dictionary<string, string?> row,
    int rowNumber,
    HashSet<string> existingExternalIdsDb,
    HashSet<string> dbNames,
    HashSet<string> selectedNames,
    Dictionary<string, Candidate> selectedByExternalId,
    HashSet<string> conflictedExternalIds,
    ClientImportResultDto result,
    Action<string, string, IEnumerable<int>, string, IEnumerable<string>?> addIssue)
    {
        string? Get(params string[] keys)
        {
            foreach (var k in keys)
            {
                var nk = NormalizeHeader(k);
                var match = row.Keys.FirstOrDefault(h => NormalizeHeader(h) == nk);
                if (match != null)
                {
                    var val = row[match];
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
            return null;
        }

        var name = Get("Name");
        var externalId = Get("ExternalId", "ID");

        var primaryMobile = Get("Primary Contact Mobile");

        // Max length checks (match EF config)
        if (ExceedsMax(primaryMobile, 32))
        {
            result.SkippedInvalid++;

            addIssue(
                "ValueTooLong",
                !string.IsNullOrWhiteSpace(externalId) ? externalId : name,
                new[] { rowNumber },
                "Primary Contact Mobile is longer than 32 characters. Row will be skipped.",
                string.IsNullOrWhiteSpace(externalId) ? null : new[] { externalId }
            );

            return;

        }

        if (string.IsNullOrWhiteSpace(name))
        {
            result.SkippedMissingName++;
            addIssue(
                "MissingName",
                "(blank)",
                new[] { rowNumber },
                "Name is required. Row skipped.",
                new[] { externalId ?? "" });
            return;
        }

        // Duplicate Name (DB or already selected in-file)
        if (dbNames.Contains(name) || selectedNames.Contains(name))
        {
            result.SkippedDuplicateName++;

            addIssue(
                "DuplicateName",
                name,
                new[] { rowNumber },
                "Duplicate. Row will be skipped.",
                string.IsNullOrWhiteSpace(externalId) ? null : new[] { externalId });

            return;
        }

        // Property Count (Total)
        var pcText = Get("Property Count (Total)");
        var propertyCountTotal = 0;
        if (!string.IsNullOrWhiteSpace(pcText))
            int.TryParse(pcText, NumberStyles.Integer, CultureInfo.InvariantCulture, out propertyCountTotal);

        var client = new Client
        {
            Name = name,
            ExternalId = externalId,

            Created = ParseDate(Get("Created")) ?? DateTime.UtcNow,
            Updated = ParseDate(Get("Updated")),
            Active = ParseBool(Get("Active")) ?? true,

            PrimaryContactName = Get("Primary Contact Name"),
            PrimaryContactEmail = Get("Primary Contact Email"),
            PrimaryContactCcEmail = Get("Primary Contact Email CC", "Primary Contact Email Cc"),
            PrimaryContactMobile = primaryMobile,
            PrimaryContactAddress = Get("Primary Contact Address"),

            BillingName = Get("Billing Contact: Organisation", "Billing Organisation", "Billing Name"),
            BillingAttentionTo = Get("Billing Contact: Attention To", "Billing Attention To"),
            BillingEmail = Get("Billing Contact: Email To", "Billing To Emails", "Billing Email"),
            BillingCcEmail = Get("Billing Contact: Email CC", "Billing CC Emails", "Billing CC Email"),
            BillingAddress = Get("Billing Contact: Address", "Billing Address"),
        };

        // No ExternalId: treat as unique by Name only
        if (string.IsNullOrWhiteSpace(externalId))
        {
            selectedNames.Add(name);
            selectedByExternalId[$"__NOEXT__{rowNumber}"] = new Candidate(client, propertyCountTotal, rowNumber);
            return;
        }

        // Duplicate ExternalId in DB -> skip
        if (existingExternalIdsDb.Contains(externalId))
        {
            result.SkippedDuplicateExternalId++;
            addIssue(
                "DuplicateExternalIdInDatabase",
                externalId,
                new[] { rowNumber },
                "Already exists in database. Row will be skipped.",
                new[] { externalId });
            return;
        }

        // ExternalId already in conflict state -> skip
        if (conflictedExternalIds.Contains(externalId))
        {
            result.SkippedDuplicateExternalIdMultipleHighPropertyCount++;
            addIssue(
                "ExternalIdConflictHighPropertyCount",
                externalId,
                new[] { rowNumber },
                "ExternalId is in conflict state (multiple rows with Property Count > 1). Row skipped.",
                new[] { externalId });
            return;
        }

        // First time ExternalId appears in file
        if (!selectedByExternalId.TryGetValue(externalId, out var existing))
        {
            selectedByExternalId[externalId] = new Candidate(client, propertyCountTotal, rowNumber);
            selectedNames.Add(name);
            return;
        }

        // Duplicate ExternalId within file
        result.SkippedDuplicateExternalIdInFile++;

        // For reporting, include BOTH "IDs":
        // - externalId is same key, but you wanted to show "ID values" to check.
        // In this case the ID value is the ExternalId itself, and both rows share it.
        addIssue(
            "DuplicateExternalIdInFile",
            externalId,
            new[] { existing.RowNumber, rowNumber },
            "Duplicate ExternalId found in import file.",
            new[] { externalId });

        var existingHigh = existing.PropertyCountTotal > 1;
        var incomingHigh = propertyCountTotal > 1;

        if (existingHigh && incomingHigh)
        {
            // Conflict: both candidates have >1 -> skip all
            conflictedExternalIds.Add(externalId);

            selectedNames.Remove(existing.Client.Name);
            selectedByExternalId.Remove(externalId);

            result.SkippedDuplicateExternalIdMultipleHighPropertyCount++;

            addIssue(
                "ExternalIdConflictHighPropertyCount",
                externalId,
                new[] { existing.RowNumber, rowNumber },
                "Multiple rows for this ExternalId have Property Count (Total) > 1. All rows for this ExternalId were skipped.",
                new[] { externalId });

            return;
        }

        if (!existingHigh && incomingHigh)
        {
            // Incoming wins if it has >1 and existing does not
            selectedNames.Remove(existing.Client.Name);
            selectedByExternalId[externalId] = new Candidate(client, propertyCountTotal, rowNumber);
            selectedNames.Add(name);
            return;
        }

        // Otherwise keep existing (either existing already >1, or both <= 1)
    }



    private static DateTime? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var formats = new[]
        {
            "d/M/yyyy H:mm",
            "dd/MM/yyyy HH:mm",
            "d/M/yyyy",
            "dd/MM/yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ"
        };

        if (DateTime.TryParseExact(input.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dt))
            return dt;

        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            return dt;

        return null;
    }

    private static bool? ParseBool(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var v = input.Trim().ToLowerInvariant();
        if (v is "true" or "t" or "yes" or "y" or "1") return true;
        if (v is "false" or "f" or "no" or "n" or "0") return false;
        return null;
    }

    private static async IAsyncEnumerable<Dictionary<string, string?>> ReadCsvRows(
        IFormFile file,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        var firstLine = await reader.ReadLineAsync(ct);
        if (firstLine == null) yield break;

        var delimiter = firstLine.Contains('\t') ? "\t" : ",";

        var rest = await reader.ReadToEndAsync(ct);
        using var sr = new StringReader(firstLine + "\n" + rest);

        using var csv = new CsvReader(sr, CultureInfo.InvariantCulture);
        csv.Context.Configuration.Delimiter = delimiter;
        csv.Context.Configuration.BadDataFound = null;
        csv.Context.Configuration.MissingFieldFound = null;
        csv.Context.Configuration.HeaderValidated = null;

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        while (await csv.ReadAsync())
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                dict[h] = csv.GetField(h);

            if (dict.Values.All(v => string.IsNullOrWhiteSpace(v)))
                continue;

            yield return dict;
        }
    }

    private static IEnumerable<Dictionary<string, string?>> ReadExcelRows(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        var headerRow = ws.FirstRowUsed();
        if (headerRow == null) yield break;

        var headers = headerRow.CellsUsed().Select(c => c.GetString()).ToList();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                dict[header] = row.Cell(i + 1).GetValue<string>();
            }

            if (dict.Values.All(v => string.IsNullOrWhiteSpace(v)))
                continue;

            yield return dict;
        }
    }

    private static string NormalizeHeader(string s)
    {
        var chars = s.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit);
        return new string(chars.ToArray());
    }

    private static async Task<int> EstimateTotalRowsAsync(IFormFile file, string ext, CancellationToken ct)
    {
        if (ext is ".xlsx")
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var used = ws.RowsUsed().Count();
            return Math.Max(0, used - 1);
        }

        int count = 0;
        await foreach (var _ in ReadCsvRows(file, ct))
            count++;

        return count;
    }

    private async Task<int> InsertClientsIgnoreConflictsAsync(
    IReadOnlyList<Client> clients,
    CancellationToken ct)
    {
        if (clients.Count == 0) return 0;

        const int batchSize = 500;
        int totalInserted = 0;

        for (int i = 0; i < clients.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = clients.Skip(i).Take(batchSize).ToList();

            // Build a single multi-row INSERT with parameters
            // We conflict-ignore on BOTH unique indexes:
            // - clients_name_key (Name unique)
            // - clients_externalid_key (ExternalId unique)
            //
            // Postgres allows multiple columns in conflict target only if there is a unique index over that set.
            // We have two separate unique indexes, so we use:
            // ON CONFLICT DO NOTHING
            // which ignores conflicts on any unique constraint.
            var sql = new System.Text.StringBuilder();
            var parameters = new List<object>();

            sql.AppendLine(@"INSERT INTO ""Clients"" (
            ""Name"",
            ""ExternalId"",
            ""Created"",
            ""Updated"",
            ""Active"",
            ""PrimaryContactName"",
            ""PrimaryContactEmail"",
            ""PrimaryContactCcEmail"",
            ""PrimaryContactMobile"",
            ""PrimaryContactAddress"",
            ""BillingName"",
            ""BillingAttentionTo"",
            ""BillingEmail"",
            ""BillingCcEmail"",
            ""BillingAddress""
        ) VALUES");

            for (int r = 0; r < batch.Count; r++)
            {
                var c = batch[r];

                // Parameter names must be unique per row
                string P(string col) => $"@p_{r}_{col}";

                sql.Append("(");
                sql.Append(string.Join(", ", new[]
                {
                P("Name"),
                P("ExternalId"),
                P("Created"),
                P("Updated"),
                P("Active"),
                P("PrimaryContactName"),
                P("PrimaryContactEmail"),
                P("PrimaryContactCcEmail"),
                P("PrimaryContactMobile"),
                P("PrimaryContactAddress"),
                P("BillingName"),
                P("BillingAttentionTo"),
                P("BillingEmail"),
                P("BillingCcEmail"),
                P("BillingAddress"),
            }));
                sql.Append(")");

                if (r < batch.Count - 1) sql.AppendLine(",");
                else sql.AppendLine();

                // Add parameters (DBNull.Value for nulls)
                parameters.Add(new Npgsql.NpgsqlParameter(P("Name"), c.Name));
                parameters.Add(new Npgsql.NpgsqlParameter(P("ExternalId"), (object?)c.ExternalId ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("Created"), c.Created));
                parameters.Add(new Npgsql.NpgsqlParameter(P("Updated"), (object?)c.Updated ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("Active"), c.Active));
                parameters.Add(new Npgsql.NpgsqlParameter(P("PrimaryContactName"), (object?)c.PrimaryContactName ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("PrimaryContactEmail"), (object?)c.PrimaryContactEmail ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("PrimaryContactCcEmail"), (object?)c.PrimaryContactCcEmail ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("PrimaryContactMobile"), (object?)c.PrimaryContactMobile ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("PrimaryContactAddress"), (object?)c.PrimaryContactAddress ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("BillingName"), (object?)c.BillingName ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("BillingAttentionTo"), (object?)c.BillingAttentionTo ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("BillingEmail"), (object?)c.BillingEmail ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("BillingCcEmail"), (object?)c.BillingCcEmail ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter(P("BillingAddress"), (object?)c.BillingAddress ?? DBNull.Value));
            }

            sql.AppendLine(@"ON CONFLICT DO NOTHING;");

            // ExecuteSqlRawAsync returns number of rows affected for INSERT in Npgsql
            var inserted = await _db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray(), ct);
            totalInserted += inserted;
        }

        return totalInserted;
    }

    static string? TrimToMax(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s;
    }

    static bool ExceedsMax(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Trim().Length > max;


}
