using FyreApp.Data;
using FyreApp.Models;

namespace FyreApp.Services.Imports.Uptick;

// Uptick's quotes, schedules, forms, accreditations and stock exports

// Uptick "quotes" export (defect quotes), shown on the property's Records tab
public class DefectQuoteImporter : RecordImporter<DefectQuote>
{
    public DefectQuoteImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.DefectQuotes;
    public override string DisplayName => "Quotes (defect quotes)";
    public override string[] SignatureHeaders => new[] { "Reference", "Total material cost", "Scope of Works" };
    protected override string Noun => "quotes";

    protected override void Map(DefectQuote q, ImportRow row)
    {
        q.Reference = Cap(Req(row.Get("Reference"), $"Quote {q.ExternalId}"), 30)!;
        q.Status = Cap(row.Get("Status")?.ToUpperInvariant(), 30);
        q.PropertyRef = Cap(row.Get("Property Ref"), 64);
        q.PropertyName = Cap(row.Get("Property"), 300);
        q.ClientName = Cap(row.Get("Client"), 300);
        q.Date = row.GetDate("Date");
        q.DueDate = row.GetDate("Due Date");
        q.ExpiryDate = row.GetDate("Expiry Date");
        q.SubmittedUtc = row.GetTimestampUtc("Submitted Date");
        q.AuthorisedDate = row.GetDate("Date Authorised");
        q.Description = row.Get("Description");
        q.ScopeOfWorks = row.Get("Scope of Works");
        q.Severity = Cap(row.Get("Severity"), 100);
        q.ServiceGroup = Cap(row.Get("Service Group"), 200);
        q.Subtotal = row.GetDecimal("Subtotal");
        q.Tax = row.GetDecimal("Tax");
        q.Total = row.GetDecimal("Total");
    }
}

// Uptick "sales quotes" export, shown on the Records tab of the property with that name
public class SalesQuoteImporter : RecordImporter<SalesQuote>
{
    public SalesQuoteImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.SalesQuotes;
    public override string DisplayName => "Sales quotes";
    public override string[] SignatureHeaders => new[] { "Template", "Work and services subtotal", "Programme maintenance subtotal sell" };
    protected override string Noun => "sales quotes";

    protected override void Map(SalesQuote q, ImportRow row)
    {
        q.Ref = Cap(Req(row.Get("Ref"), $"Sales quote {q.ExternalId}"), 30)!;
        q.Status = Cap(row.Get("Status")?.ToUpperInvariant(), 30);
        q.PropertyName = Cap(row.Get("Property")?.Trim(), 300);
        q.Address = row.Get("Address")?.Trim();
        q.ClientName = Cap(row.Get("Client"), 300);
        q.Description = row.Get("Description");
        q.ScopeOfWorksHtml = row.Get("Scope of Works");
        q.Template = Cap(row.Get("Template"), 200);
        q.Created = row.GetDate("Created");
        q.ExpiryDate = row.GetDate("Expiry date");
        q.Subtotal = row.GetDecimal("Work and services subtotal");
    }
}

// Uptick "form responses" export, shown on the task they were submitted on
public class FormResponseImporter : RecordImporter<FormResponse>
{
    public FormResponseImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.FormResponses;
    public override string DisplayName => "Form responses";
    public override string[] SignatureHeaders => new[] { "target_contenttype", "target_object_id", "template_version" };
    protected override string IdHeader => "id";
    protected override string Noun => "form responses";

    protected override void Map(FormResponse f, ImportRow row)
    {
        f.Ref = Cap(row.Get("ref"), 30);
        f.SubmittedUtc = row.GetTimestampUtc("submitted");
        f.SubmitterExternalId = Cap(row.Get("submitter"), 64);
        f.TemplateVersion = Cap(row.Get("template_version"), 64);
        f.TargetContentType = Cap(row.Get("target_contenttype"), 20);
        f.TargetObjectId = Cap(row.Get("target_object_id"), 64);
        f.IsComplete = row.GetBool("is_complete") ?? false;
    }
}

// Uptick "accreditations" export: technicians' licences, shown on the Users page
public class AccreditationImporter : RecordImporter<Accreditation>
{
    public AccreditationImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.Accreditations;
    public override string DisplayName => "Accreditations";
    public override string[] SignatureHeaders => new[] { "Expiry", "Technician", "Number" };
    protected override string Noun => "accreditations";

    protected override void Map(Accreditation a, ImportRow row)
    {
        a.Type = Cap(row.Get("Type"), 200);
        a.Number = Cap(row.Get("Number"), 500);
        a.Expiry = row.GetDate("Expiry");
        a.Technician = Cap(row.Get("Technician"), 200);
        a.PropertyRef = Cap(row.Get("Property Ref"), 64);
    }
}

// Uptick "products" export, on the Products page
public class ProductImporter : RecordImporter<Product>
{
    public ProductImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.Products;
    public override string DisplayName => "Products";
    public override string[] SignatureHeaders => new[] { "Unit Description", "Default Price", "Stocked" };
    protected override string Noun => "products";

    protected override void Map(Product p, ImportRow row)
    {
        p.Name = Cap(Req(row.Get("Name"), $"Product {p.ExternalId}"), 300)!;
        p.Type = Cap(row.Get("Type"), 20);
        p.Description = row.Get("Description");
        p.UnitDescription = Cap(row.Get("Unit Description"), 300);
        p.Code = Cap(row.Get("Code"), 100);
        p.Sku = Cap(row.Get("SKU"), 100);
        p.Supplier = Cap(row.Get("Supplier"), 300);
        p.Category = Cap(row.Get("Category"), 200);
        p.Subcategory = Cap(row.Get("Subcategory"), 200);
        p.CostPrice = row.GetDecimal("Cost Price");
        p.DefaultPrice = row.GetDecimal("Default Price");
        p.Active = row.GetBool("Active") ?? true;
        p.Stocked = row.GetBool("Stocked") ?? false;
        p.Starred = row.GetBool("Starred") ?? false;
        p.LastUsedUtc = row.GetTimestampUtc("Last Used");
    }
}

// Uptick "warehouses" export (the warehouse and technicians' vans), on the Products page
public class WarehouseImporter : RecordImporter<Warehouse>
{
    public WarehouseImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.Warehouses;
    public override string DisplayName => "Warehouses";
    public override string[] SignatureHeaders => new[] { "Manager", "Reserved (Incoming Stock)", "Stock Value" };
    protected override string Noun => "warehouses";

    protected override void Map(Warehouse w, ImportRow row)
    {
        w.Name = Cap(Req(row.Get("Name"), $"Warehouse {w.ExternalId}"), 200)!;
        w.Type = Cap(row.Get("Type"), 30);
        w.Branch = Cap(row.Get("Branch"), 200);
        w.Manager = Cap(row.Get("Manager"), 200);
        w.StockValue = row.GetDecimal("Stock Value");
    }
}

// Uptick "annotated stocks" export: stock on hand per warehouse. No IDs, so each import replaces it.
public class StockLevelImporter : SnapshotImporter<StockLevel>
{
    public StockLevelImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.StockLevels;
    public override string DisplayName => "Stock (annotated stocks)";
    public override string[] SignatureHeaders => new[] { "Warehouse", "On Hand", "Restock Level" };
    protected override string Noun => "stock lines";

    protected override StockLevel? Build(ImportRow row, ImportContext ctx)
    {
        var warehouse = row.Get("Warehouse");
        var product = row.Get("Product");
        if (warehouse == null || product == null)
        {
            ctx.Skip("Missing warehouse or product", "(blank)", row.RowNumber, "Stock line has no warehouse or product. Skipped.");
            return null;
        }
        return new StockLevel
        {
            Warehouse = Cap(warehouse, 200)!,
            Product = Cap(product, 300)!,
            SupplierRef = Cap(row.Get("Supplier Ref"), 100),
            InternalRef = Cap(row.Get("Internal Ref"), 100),
            Category = Cap(row.Get("Category"), 200),
            Supplier = Cap(row.Get("Supplier"), 300),
            OnHand = row.GetDecimal("On Hand"),
            Available = row.GetDecimal("Available"),
            Reserved = row.GetDecimal("Reserved"),
            OnOrder = row.GetDecimal("On Order"),
            RestockLevel = row.GetDecimal("Restock Level"),
            Required = row.GetDecimal("Required"),
            BinLocation = Cap(row.Get("Bin Location"), 100),
            StockValue = row.GetDecimal("Stock Value"),
            UptickData = row.ToJson()
        };
    }
}

// Uptick "schedules" export: a property's fire safety schedules. No IDs, so each import replaces them.
public class PropertyScheduleImporter : SnapshotImporter<PropertySchedule>
{
    public PropertyScheduleImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.PropertySchedules;
    public override string DisplayName => "Schedules (property)";
    public override string[] SignatureHeaders => new[] { "Schedule Number", "Schedule Date", "Issuer" };
    protected override string Noun => "property schedules";

    protected override PropertySchedule? Build(ImportRow row, ImportContext ctx) => new()
    {
        PropertyExternalId = Cap(row.Get("Property Id"), 64),
        PropertyRef = Cap(row.Get("Property Ref"), 64),
        PropertyName = Cap(row.Get("Property Name"), 300),
        Number = Cap(row.Get("Schedule Number"), 100),
        Date = row.GetDate("Schedule Date"),
        Type = Cap(row.Get("Schedule Type"), 100),
        Issuer = Cap(row.Get("Issuer"), 300),
        UptickData = row.ToJson()
    };
}
