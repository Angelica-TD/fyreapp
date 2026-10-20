using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// Who Uptick bills (Uptick "billingcards"). Clients and properties link to theirs.
public class BillingCard : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    [Required, StringLength(300)] public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
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
}

// A recurring billing arrangement for a property (Uptick "billingcontracts")
public class BillingContract : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int? PropertyId { get; set; }
    public Property? Property { get; set; }

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
}

// One line of a billing contract (Uptick "billingcontractlineitems")
public class BillingContractLineItem : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int? BillingContractId { get; set; }
    public BillingContract? BillingContract { get; set; }

    [StringLength(300)] public string? Product { get; set; }
    public string? Description { get; set; }
    public decimal? UnitPrice { get; set; }

    // "Per visit", ...
    [StringLength(50)] public string? Type { get; set; }
    [StringLength(300)] public string? RoutineType { get; set; }
    [StringLength(100)] public string? LevelType { get; set; }
    public bool Visible { get; set; }
}

// An Uptick invoice (Uptick "invoices"), for a client, usually at a property and for a task
public class Invoice : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

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

    // The task's ref
    [StringLength(20)] public string? TaskRef { get; set; }

    public DateTime? Date { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }
    public decimal? CreditTotal { get; set; }

    // DRAFT, SUBMITTED, AUTHORISED, PAID, VOIDED, ...
    [StringLength(30)] public string? Status { get; set; }
}

// A credit against an invoice (Uptick "creditnotes")
public class CreditNote : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal? Total { get; set; }

    [Required, StringLength(50)] public string Number { get; set; } = string.Empty;
    [StringLength(30)] public string? Status { get; set; }
    public DateTime? Date { get; set; }
    public string? Description { get; set; }

    // The invoice's number and property
    [StringLength(50)] public string? InvoiceNumber { get; set; }
    [StringLength(64)] public string? PropertyRef { get; set; }
}
