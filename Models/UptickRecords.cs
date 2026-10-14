using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// An Uptick quote to fix defects at a property (Uptick "quotes" export; "Defect quotes" in Uptick). Separate from
// FyreApp's own quotes. Remarks name theirs by ref ("Quote Ref").
public class DefectQuote : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    // e.g. "Q-2785"
    [Required, StringLength(30)] public string Reference { get; set; } = string.Empty;

    // DRAFT, SUBMITTED, ACTIONED, FINALISED, DECLINED, EXPIRED, VOIDED, DELETED
    [StringLength(30)] public string? Status { get; set; }

    [StringLength(64)] public string? PropertyRef { get; set; }
    [StringLength(300)] public string? PropertyName { get; set; }
    [StringLength(300)] public string? ClientName { get; set; }

    public DateTime? Date { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? AuthorisedDate { get; set; }

    public string? Description { get; set; }
    public string? ScopeOfWorks { get; set; }
    [StringLength(100)] public string? Severity { get; set; }
    [StringLength(200)] public string? ServiceGroup { get; set; }

    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }

    public string? UptickData { get; set; }
}

// An Uptick sales quote (Uptick "sales quotes" export). The export names the property and client but not their
// refs, so it's matched to a property by name.
public class SalesQuote : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    // e.g. "SQ-0361"
    [Required, StringLength(30)] public string Ref { get; set; } = string.Empty;

    // DRAFT, SUBMITTED, APPROVED, DECLINED, EXPIRED, VOIDED, FINALISED, COMPLETED
    [StringLength(30)] public string? Status { get; set; }

    [StringLength(300)] public string? PropertyName { get; set; }
    public string? Address { get; set; }
    [StringLength(300)] public string? ClientName { get; set; }
    public string? Description { get; set; }

    // Uptick keeps the scope of works as HTML
    public string? ScopeOfWorksHtml { get; set; }
    [StringLength(200)] public string? Template { get; set; }

    public DateTime? Created { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal? Subtotal { get; set; }

    public string? UptickData { get; set; }
}

// A fire safety schedule held for a property (Uptick "schedules" export)
public class PropertySchedule
{
    public int Id { get; set; }

    [StringLength(64)] public string? PropertyExternalId { get; set; }
    [StringLength(64)] public string? PropertyRef { get; set; }
    [StringLength(300)] public string? PropertyName { get; set; }

    [StringLength(100)] public string? Number { get; set; }
    public DateTime? Date { get; set; }
    [StringLength(100)] public string? Type { get; set; }
    [StringLength(300)] public string? Issuer { get; set; }

    public string? UptickData { get; set; }
}

// A form submitted in Uptick (Uptick "form responses" export). The export has no answers, just which form was
// submitted on what, by whom and when. Uptick record type 28 is a task (target = the task's Uptick ID).
public class FormResponse : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    // e.g. "F-00116"
    [StringLength(30)] public string? Ref { get; set; }
    public DateTime? SubmittedUtc { get; set; }

    // The submitter's Uptick user ID
    [StringLength(64)] public string? SubmitterExternalId { get; set; }
    [StringLength(64)] public string? TemplateVersion { get; set; }

    [StringLength(20)] public string? TargetContentType { get; set; }
    [StringLength(64)] public string? TargetObjectId { get; set; }
    public bool IsComplete { get; set; }

    public string? UptickData { get; set; }

    public const string TaskContentType = "28";
}

// A technician's licence or accreditation held in Uptick (Uptick "accreditations" export), e.g.
// "Licence No.: QBCC 15366855 | FPA 25023". Matched to the Uptick user by name.
public class Accreditation : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    [StringLength(200)] public string? Type { get; set; }
    [StringLength(500)] public string? Number { get; set; }
    public DateTime? Expiry { get; set; }

    // As Uptick names the user, e.g. "Wes Speedon (Field Tech)"
    [StringLength(200)] public string? Technician { get; set; }
    [StringLength(64)] public string? PropertyRef { get; set; }

    public string? UptickData { get; set; }
}

// Something Uptick sells or uses (Uptick "products" export)
public class Product : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    [Required, StringLength(300)] public string Name { get; set; } = string.Empty;

    // M (materials), L (labour), ...
    [StringLength(20)] public string? Type { get; set; }
    public string? Description { get; set; }
    [StringLength(300)] public string? UnitDescription { get; set; }
    [StringLength(100)] public string? Code { get; set; }
    [StringLength(100)] public string? Sku { get; set; }
    [StringLength(300)] public string? Supplier { get; set; }
    [StringLength(200)] public string? Category { get; set; }
    [StringLength(200)] public string? Subcategory { get; set; }

    public decimal? CostPrice { get; set; }
    public decimal? DefaultPrice { get; set; }
    public bool Active { get; set; } = true;
    public bool Stocked { get; set; }
    public bool Starred { get; set; }
    public DateTime? LastUsedUtc { get; set; }

    public string? UptickData { get; set; }

    public string TypeLabel => Type?.ToUpperInvariant() switch
    {
        "M" => "Materials",
        "L" => "Labour",
        "S" => "Service",
        null or "" => "-",
        var t => t
    };
}

// A stock location (Uptick "warehouses" export): the warehouse or a technician's van
public class Warehouse : IUptickRecord
{
    public int Id { get; set; }
    [StringLength(64)] public string? ExternalId { get; set; }

    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;

    // FACILITY or VAN
    [StringLength(30)] public string? Type { get; set; }
    [StringLength(200)] public string? Branch { get; set; }
    [StringLength(200)] public string? Manager { get; set; }
    public decimal? StockValue { get; set; }

    public string? UptickData { get; set; }
}

// How much of a product a warehouse holds (Uptick "annotated stocks" export). The export has no IDs, so each
// import replaces the lot: it's a snapshot.
public class StockLevel
{
    public int Id { get; set; }

    [Required, StringLength(200)] public string Warehouse { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Product { get; set; } = string.Empty;
    [StringLength(100)] public string? SupplierRef { get; set; }
    [StringLength(100)] public string? InternalRef { get; set; }
    [StringLength(200)] public string? Category { get; set; }
    [StringLength(300)] public string? Supplier { get; set; }

    public decimal? OnHand { get; set; }
    public decimal? Available { get; set; }
    public decimal? Reserved { get; set; }
    public decimal? OnOrder { get; set; }
    public decimal? RestockLevel { get; set; }
    public decimal? Required { get; set; }
    [StringLength(100)] public string? BinLocation { get; set; }
    public decimal? StockValue { get; set; }

    public string? UptickData { get; set; }
}
