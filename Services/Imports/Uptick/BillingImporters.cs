using FyreApp.Data;
using FyreApp.Models;

namespace FyreApp.Services.Imports.Uptick;

// Uptick's billing exports: billing cards, billing contracts and their line items, invoices and credit notes.
// Shown on the property's Billing tab (and invoices on their task), matched by Uptick refs.

public class BillingCardImporter : RecordImporter<BillingCard>
{
    public BillingCardImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.BillingCards;
    public override string DisplayName => "Billing cards";
    public override string[] SignatureHeaders => new[] { "Postal Attention", "Attached Clients", "Default Tax Code" };
    protected override string Noun => "billing cards";

    protected override void Map(BillingCard c, ImportRow row)
    {
        c.Name = Cap(Req(row.Get("Name"), $"Billing card {c.ExternalId}"), 300)!;
        c.Ref = Cap(row.Get("Ref"), 100);
        c.BusinessNumber = Cap(row.Get("Business Number"), 50);
        c.PostalAttention = Cap(row.Get("Postal Attention"), 200);
        c.PostalEmail = Cap(row.Get("Postal Email"), 300);
        c.PostalMobile = Cap(row.Get("Postal Mobile"), 50);
        c.PostalBusinessHours = Cap(row.Get("Postal Business Hours"), 50);
        c.PostalAddress = row.Get("Postal Address")?.Trim();
        c.SiteAddress = row.Get("Site Address")?.Trim();
        c.NumberOfInvoices = row.GetInt("Number of Invoices");
        c.AccountingOrganisation = Cap(row.Get("Accounting Organisation"), 200);
        c.DefaultTaxCode = Cap(row.Get("Default Tax Code"), 50);
        c.CreditLimit = row.GetDecimal("Credit Limit");
    }
}

public class BillingContractImporter : RecordImporter<BillingContract>
{
    public BillingContractImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.BillingContracts;
    public override string DisplayName => "Billing contracts";
    public override string[] SignatureHeaders => new[] { "Invoice mode", "Annual value", "Price increase policy" };
    protected override string Noun => "billing contracts";

    protected override void Map(BillingContract c, ImportRow row)
    {
        c.Name = Cap(Req(row.Get("Name"), $"Billing contract {c.ExternalId}"), 300)!;
        c.Active = row.GetBool("Active") ?? false;
        c.Type = Cap(row.Get("Type"), 50);
        c.Frequency = Cap(row.Get("Frequency"), 50);
        c.InvoiceMode = Cap(row.Get("Invoice mode"), 50);
        c.PropertyRef = Cap(row.Get("Property ref"), 64);
        c.PropertyName = Cap(row.Get("Property"), 300);
        c.ClientName = Cap(row.Get("Client"), 300);
        c.BillingCardName = Cap(row.Get("Billing Card Name"), 300);
        c.StartDate = row.GetDate("Start date");
        c.FinishDate = row.GetDate("Finish date");
        c.NextDue = row.GetDate("Next due");
        c.PeriodBegin = row.GetDate("Period begin");
        c.PeriodEnd = row.GetDate("Period end");
        c.ReviewDate = row.GetDate("Review Date");
        c.AnnualValue = row.GetDecimal("Annual value");
        c.Notes = row.Get("Notes");
    }
}

public class BillingContractLineItemImporter : RecordImporter<BillingContractLineItem>
{
    public BillingContractLineItemImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.BillingContractLineItems;
    public override string DisplayName => "Billing contract line items";
    public override string[] SignatureHeaders => new[] { "Billing Contract ID", "Unit Price", "Price Period" };
    protected override string Noun => "billing contract line items";

    protected override void Map(BillingContractLineItem l, ImportRow row)
    {
        l.ContractExternalId = Cap(row.Get("Billing Contract ID"), 64);
        l.Product = Cap(row.Get("Product"), 300);
        l.Description = row.Get("Description");
        l.UnitPrice = row.GetDecimal("Unit Price");
        l.Type = Cap(row.Get("Type"), 50);
        l.RoutineType = Cap(row.Get("Routine Type"), 300);
        l.LevelType = Cap(row.Get("Level Type"), 100);
        l.Visible = row.GetBool("Visible") ?? true;
    }
}

public class InvoiceImporter : RecordImporter<Invoice>
{
    public InvoiceImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.Invoices;
    public override string DisplayName => "Invoices";
    public override string[] SignatureHeaders => new[] { "Number", "Invoice Ref", "Credit Total" };
    protected override string Noun => "invoices";

    protected override void Map(Invoice i, ImportRow row)
    {
        i.Number = Cap(Req(row.Get("Number"), $"Invoice {i.ExternalId}"), 50)!;
        i.InvoiceRef = Cap(row.Get("Invoice Ref"), 100);
        i.Description = row.Get("Description");
        i.PropertyRef = Cap(row.Get("Property Ref"), 64);
        i.PropertyName = Cap(row.Get("Property"), 300);
        i.ClientName = Cap(row.Get("Client"), 300);
        i.BillingCardName = Cap(row.Get("Billing Card Name"), 300);
        i.TaskExternalId = Cap(row.Get("Task ID"), 64);
        i.TaskRef = Cap(row.Get("Task Ref"), 20);
        i.Date = row.GetDate("Date");
        i.DueDate = row.GetDate("Due Date");
        i.Subtotal = row.GetDecimal("Subtotal");
        i.Tax = row.GetDecimal("Tax");
        i.Total = row.GetDecimal("Total");
        i.CreditTotal = row.GetDecimal("Credit Total");
        i.Status = Cap(row.Get("Status")?.ToUpperInvariant(), 30);
    }
}

public class CreditNoteImporter : RecordImporter<CreditNote>
{
    public CreditNoteImporter(AppDbContext db) : base(db) { }
    public override UptickExportType Type => UptickExportType.CreditNotes;
    public override string DisplayName => "Credit notes";
    public override string[] SignatureHeaders => new[] { "number", "invoice_id", "invoice__number" };
    protected override string IdHeader => "id";
    protected override string Noun => "credit notes";

    protected override void Map(CreditNote c, ImportRow row)
    {
        c.Number = Cap(Req(row.Get("number"), $"Credit note {c.ExternalId}"), 50)!;
        c.Status = Cap(row.Get("status")?.ToUpperInvariant(), 30);
        c.Date = row.GetDate("date");
        c.Description = row.Get("description");
        c.InvoiceExternalId = Cap(row.Get("invoice_id"), 64);
        c.InvoiceNumber = Cap(row.Get("invoice__number"), 50);
        c.PropertyRef = Cap(row.Get("invoice__property__ref"), 64);
    }
}
