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
        ("Routine schedules", new[] { "Routine", "Due Date", "Property ref" })
    };

    // Exports whose records come from Uptick's API now (the Uptick migration page): their file imports are off.
    // Recognised so an upload says so instead of failing to recognise the file.
    public static readonly (UptickExportType? Type, string Name, string[] Headers)[] FromApi =
    {
        (null, "Clients", new[] { "Name", "Property Count (Total)", "Primary Contact Name" }),
        (UptickExportType.Properties, "Properties", new[] { "Ref", "Name", "Client ID", "Address Street Address" }),
        (UptickExportType.PropertyContacts, "Property contacts", new[] { "Property Ref", "Contact Name", "Role", "Report req" }),
        (UptickExportType.ClientContacts, "Client contacts", new[] { "Client ID", "Client Name", "Position", "Phone" }),
        (UptickExportType.Assets, "Assets", new[] { "Label", "Property Ref", "Guid", "Type", "Variant" }),
        (UptickExportType.AssetTypes, "Asset types", new[] { "Name", "Sequence Pattern", "Applicable Routines" }),
        (UptickExportType.AssetTypeVariants, "Asset type variants", new[] { "Name", "Asset Type ID", "Default Replacement Product" }),
        (UptickExportType.Users, "Users", new[] { "Date joined", "Last login", "Security Groups" }),
        (UptickExportType.RoutineServiceTypes, "Routine service types", new[] { "Name", "Standard Reference", "Default Performance Standard" }),
        (UptickExportType.RoutineServiceLevels, "Routine service levels", new[] { "Display Code", "Routine Service Type ID", "Interval" }),
        (UptickExportType.RemarkTypes, "Remark types", new[] { "Label", "Asset Type Tag", "Owner Responsible" }),
        (UptickExportType.Tasks, "Tasks", new[] { "Ref", "Category", "Scope of works", "Property Ref", "Due" }),
        (UptickExportType.Remarks, "Remarks", new[] { "Remark Type", "Property Ref", "Severity", "Resolution" }),
        (UptickExportType.Reports, "Reports", new[] { "Report Type", "Property Ref", "Issued", "Inspected" }),
        (UptickExportType.Products, "Products", new[] { "Unit Description", "Default Price", "Stocked" }),
        (UptickExportType.Warehouses, "Warehouses", new[] { "Manager", "Reserved (Incoming Stock)", "Stock Value" }),
        (UptickExportType.Stocks, "Stock", new[] { "Warehouse", "On Hand", "Restock Level" }),
        (UptickExportType.DefectQuotes, "Quotes", new[] { "Reference", "Total material cost", "Scope of Works" }),
        (UptickExportType.ServiceQuotes, "Sales quotes", new[] { "Template", "Work and services subtotal", "Programme maintenance subtotal sell" }),
        (UptickExportType.Invoices, "Invoices", new[] { "Number", "Invoice Ref", "Credit Total" }),
        (UptickExportType.CreditNotes, "Credit notes", new[] { "number", "invoice_id", "invoice__number" }),
        (UptickExportType.BillingCards, "Billing cards", new[] { "Postal Attention", "Attached Clients", "Default Tax Code" }),
        (UptickExportType.BillingContracts, "Billing contracts", new[] { "Invoice mode", "Annual value", "Price increase policy" }),
        (UptickExportType.BillingContractLineItems, "Billing contract line items", new[] { "Billing Contract ID", "Unit Price", "Price Period" }),
    };

    public static string FromApiMessage(string name) =>
        $"{name} come from Uptick's API now (Admin → Uptick migration), so their file import is off.";

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

        TabularFileReader.Table table;
        try
        {
            table = await TabularFileReader.ReadTableAsync(stream, fileName, ct);
        }
        catch (Exception ex)
        {
            result.Error = $"Could not read file: {ex.Message}";
            return result;
        }

        // From the header row, so an export with no rows is still recognised
        var raw = table.Rows;
        var headers = table.Headers.Select(TabularFileReader.NormalizeHeader).ToHashSet();

        bool Matches(string[] signature) =>
            signature.All(h => headers.Contains(TabularFileReader.NormalizeHeader(h)));

        UptickImporter? importer;
        if (type != null)
        {
            importer = Importers.FirstOrDefault(i => i.Type == type);
            if (importer == null)
            {
                result.Error = FromApiMessage(FromApi.FirstOrDefault(f => f.Type == type).Name ?? type.ToString()!);
                return result;
            }
            if (headers.Count > 0 && !importer.Recognises(headers))
            {
                var missing = importer.SignatureHeaders.Where(h => !headers.Contains(TabularFileReader.NormalizeHeader(h)));
                result.Error = $"This doesn't look like an Uptick {importer.DisplayName} export (missing columns: {string.Join(", ", missing)}).";
                return result;
            }
        }
        else
        {
            importer = Importers.FirstOrDefault(i => i.Recognises(headers));
            if (importer == null && FromApi.FirstOrDefault(f => Matches(f.Headers)).Name is { } fromApi)
            {
                result.Error = FromApiMessage(fromApi);
                return result;
            }
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
            await importer.RunAsync(rows, dryRun, ctx, ct);
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
