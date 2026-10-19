using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A record imported from an Uptick export: its Uptick ID, and every column of its row as JSON (key order kept)
public interface IUptickRecord
{
    string? ExternalId { get; set; }
    string? UptickData { get; set; }
}

// Who Uptick bills (Uptick "billing cards" export). Clients and properties name theirs by ID in their own
// exports ("Billingcard ID", "Billing Card ID"), which is how the Billing tab finds them.
public class BillingCard : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    [Required, StringLength(300)] public string Name { get; set; } = string.Empty;
    [StringLength(100)] public string? Ref { get; set; }
    [StringLength(50)] public string? BusinessNumber { get; set; }

    [StringLength(200)] public string? PostalAttention { get; set; }
    [StringLength(300)] public string? PostalEmail { get; set; }
    [StringLength(50)] public string? PostalMobile { get; set; }
    [StringLength(50)] public string? PostalBusinessHours { get; set; }
    public string? PostalAddress { get; set; }
    public string? SiteAddress { get; set; }

    public int? NumberOfInvoices { get; set; }
    [StringLength(200)] public string? AccountingOrganisation { get; set; }
    [StringLength(50)] public string? DefaultTaxCode { get; set; }
    public decimal? CreditLimit { get; set; }

    public string? UptickData { get; set; }
}

// A recurring billing arrangement for a property (Uptick "billing contracts" export)
public class BillingContract : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    [Required, StringLength(300)] public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }

    // Fixed, ...; Quarterly, ...; Advance / Arrears
    [StringLength(50)] public string? Type { get; set; }
    [StringLength(50)] public string? Frequency { get; set; }
    [StringLength(50)] public string? InvoiceMode { get; set; }

    [StringLength(64)] public string? PropertyRef { get; set; }
    [StringLength(300)] public string? PropertyName { get; set; }
    [StringLength(300)] public string? ClientName { get; set; }
    [StringLength(300)] public string? BillingCardName { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? FinishDate { get; set; }
    public DateTime? NextDue { get; set; }
    public DateTime? PeriodBegin { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public DateTime? ReviewDate { get; set; }
    public decimal? AnnualValue { get; set; }
    public string? Notes { get; set; }

    public string? UptickData { get; set; }
}

// One line of a billing contract (Uptick "billing contract line items" export)
public class BillingContractLineItem : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    // The contract's Uptick ID ("Billing Contract ID")
    [StringLength(64)] public string? ContractExternalId { get; set; }

    [StringLength(300)] public string? Product { get; set; }
    public string? Description { get; set; }
    public decimal? UnitPrice { get; set; }

    // "Per visit", ...
    [StringLength(50)] public string? Type { get; set; }
    [StringLength(300)] public string? RoutineType { get; set; }
    [StringLength(100)] public string? LevelType { get; set; }
    public bool Visible { get; set; }

    public string? UptickData { get; set; }
}

// An Uptick invoice (Uptick "invoices"), for a client, usually at a property and for a task
public class Invoice : IUptickRecord, IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    public int? PropertyId { get; set; }
    public Property? Property { get; set; }
    public int? ClientTaskId { get; set; }
    public ClientTask? ClientTask { get; set; }

    // e.g. "IV000032823"
    [Required, StringLength(50)] public string Number { get; set; } = string.Empty;
    [StringLength(100)] public string? InvoiceRef { get; set; }
    public string? Description { get; set; }

    [StringLength(64)] public string? PropertyRef { get; set; }
    [StringLength(300)] public string? PropertyName { get; set; }
    [StringLength(300)] public string? ClientName { get; set; }
    [StringLength(300)] public string? BillingCardName { get; set; }

    // The task's Uptick ID ("Task ID"), and its ref
    [StringLength(64)] public string? TaskExternalId { get; set; }
    [StringLength(20)] public string? TaskRef { get; set; }

    public DateTime? Date { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }
    public decimal? CreditTotal { get; set; }

    // DRAFT, SUBMITTED, AUTHORISED, PAID, VOIDED, ...
    [StringLength(30)] public string? Status { get; set; }

    public string? UptickData { get; set; }
}

// A credit against an invoice (Uptick "creditnotes")
public class CreditNote : IUptickRecord, IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal? Total { get; set; }

    [Required, StringLength(50)] public string Number { get; set; } = string.Empty;
    [StringLength(30)] public string? Status { get; set; }
    public DateTime? Date { get; set; }
    public string? Description { get; set; }

    // The invoice's Uptick ID ("invoice_id"), its number and property
    [StringLength(64)] public string? InvoiceExternalId { get; set; }
    [StringLength(50)] public string? InvoiceNumber { get; set; }
    [StringLength(64)] public string? PropertyRef { get; set; }

    public string? UptickData { get; set; }
}
