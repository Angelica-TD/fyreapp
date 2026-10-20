using System;
using System.ComponentModel.DataAnnotations;
using FyreApp.Infrastructure;

namespace FyreApp.Models;

public class Client : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Who Uptick bills for it
    public int? BillingCardId { get; set; }
    public BillingCard? BillingCard { get; set; }

    // Used for import duplicate detection (maps from export "ID")
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "C-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Created because another export referenced this record but it wasn't in its own export
    // (e.g. archived in Uptick). Inactive; filled in when the record itself is imported.
    public bool IsPlaceholder { get; set; }

    // Shown to users: the Uptick ID if imported, otherwise the FyreApp ref
    public string? DisplayRef => string.IsNullOrWhiteSpace(ExternalId) ? FyreRef : ExternalId;

    public string Name { get; set; } = string.Empty;

    // Auditing / status
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public bool Active { get; set; } = true;

    // Primary contact
    [Display(Name = "Primary contact")]
    [UiField(Order = 1)]
    public string? PrimaryContactName { get; set; }

    [Display(Name = "Email")]
    [UiField(Order = 2)]
    public string? PrimaryContactEmail { get; set; }

    [Display(Name = "Mobile")]
    [UiField(Order = 3)]
    public string? PrimaryContactMobile { get; set; }

    // Uptick "Primary Contact BH" / "Primary Contact AH"
    [Display(Name = "Phone")]
    [UiField(Order = 4)]
    public string? PrimaryContactPhone { get; set; }

    [Display(Name = "After hours")]
    [UiField(Order = 5)]
    public string? PrimaryContactAfterHours { get; set; }

    public string? PrimaryContactCcEmail { get; set; }
    public string? PrimaryContactAddress { get; set; }

    // Uptick "Reference" (often a trading name) and "Primary Sector", e.g. "Property Management (Residential)"
    public string? Reference { get; set; }
    public string? PrimarySector { get; set; }

    // Office notes and billing instructions (Uptick "Notes", "Billing Requirements")
    public string? Notes { get; set; }
    public string? BillingRequirements { get; set; }

    // Who gets reports, when not the primary contact (Uptick "Report Contact: …")
    public string? ReportOrganisation { get; set; }
    public string? ReportAttentionTo { get; set; }
    public string? ReportAddress { get; set; }
    public string? ReportEmail { get; set; }
    public string? ReportCcEmail { get; set; }

    // Billing
    public string? BillingName { get; set; }
    public string? BillingAttentionTo { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingCcEmail { get; set; }

    //the exported data only has one field for billing address
    public string? BillingAddress { get; set; }

    // Navigation
    public ICollection<Property> Properties { get; set; } = new List<Property>();
    public ICollection<ClientContact> Contacts { get; set; } = new List<ClientContact>();
}
