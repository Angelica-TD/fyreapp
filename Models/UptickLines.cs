using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// The lines of Uptick's quotes and invoices, and a remark's history. Each is on its quote, invoice or remark and is
// removed when deleted in Uptick.

// A defect quote's line ("defectquotelineitems"): a product or labour for a remark on an asset
public class DefectQuoteLine : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int DefectQuoteId { get; set; }
    public DefectQuote DefectQuote { get; set; } = null!;

    public int Index { get; set; }
    public string? Description { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }
    public int? RemarkId { get; set; }
    public Remark? Remark { get; set; }

    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }
}

// An invoice's line ("invoicelineitems")
public class InvoiceLine : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public int Index { get; set; }
    public string? Description { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }

    // e.g. "4-1080"
    [StringLength(50)] public string? AccountCode { get; set; }
}

// A sales quote's line, from one of Uptick's three line endpoints: a product ("servicequoteproductlineitems"), a
// routine service at a fixed annual price ("servicequotefixedlineitems") or one charged per service
// ("servicequotedoandchargelineitems"). Their ids are each endpoint's own, so a line is known by kind and id.
public class ServiceQuoteLine
{
    public const string ProductKind = "product";
    public const string FixedKind = "fixed";
    public const string DoAndChargeKind = "doandcharge";

    public int Id { get; set; }
    [StringLength(20)] public string Kind { get; set; } = ProductKind;
    public long UptickId { get; set; }

    public int ServiceQuoteId { get; set; }
    public ServiceQuote ServiceQuote { get; set; } = null!;

    public int Index { get; set; }
    public string? Description { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? RoutineServiceTypeId { get; set; }
    public RoutineServiceType? RoutineServiceType { get; set; }

    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? CostPrice { get; set; }

    // A product's price; a routine service's is a year's
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }

    // A routine service's estimated time on site per service ("00:30:00")
    [StringLength(20)] public string? EstimatedDuration { get; set; }
}

// A step in a remark's life ("remarkevents"): added, verified or resolved, by whom, on which task
public class RemarkEvent : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int RemarkId { get; set; }
    public Remark Remark { get; set; } = null!;

    // Uptick's code (A, V, R) and what it shows ("Added", "Verified", "Resolved")
    [StringLength(5)] public string? Event { get; set; }
    [StringLength(50)] public string? Label { get; set; }
    public string? Notes { get; set; }
    public DateTime? WhenUtc { get; set; }

    public int? UptickUserId { get; set; }
    public UptickUser? UptickUser { get; set; }
    public int? ClientTaskId { get; set; }
    public ClientTask? ClientTask { get; set; }
}
