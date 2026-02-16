using System;
using System.ComponentModel.DataAnnotations;
using FyreApp.Infrastructure;

namespace FyreApp.Models;

public class Client
{
    public int Id { get; set; }

    // Used for import duplicate detection (maps from export "ID")
    public string? ExternalId { get; set; }

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
    public string? PrimaryContactCcEmail { get; set; }
    public string? PrimaryContactAddress { get; set; }

    // Billing
    public string? BillingName { get; set; }
    public string? BillingAttentionTo { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingCcEmail { get; set; }

    //the exported data only has one field for billing address
    public string? BillingAddress { get; set; }

    // Navigation
    public ICollection<Site> Sites { get; set; } = new List<Site>();
}
