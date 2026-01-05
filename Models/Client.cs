using System;

namespace FyreApp.Models;

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Auditing / status
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public bool Active { get; set; } = true;

    // Primary contact
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? PrimaryContactMobile { get; set; }
    public string? PrimaryContactCcEmail { get; set; }

    // Primary address
    public string? PrimaryStreetAddress { get; set; }
    public string? PrimarySuburb { get; set; }
    public string? PrimaryPostcode { get; set; }
    public string? PrimaryState { get; set; }

    // Billing
    public string? BillingName { get; set; }
    public string? BillingAttentionTo { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingCcEmail { get; set; }
    public string? BillingStreetAddress { get; set; }
    public string? BillingSuburb { get; set; }
    public string? BillingPostcode { get; set; }
    public string? BillingState { get; set; }

    // Navigation
    public ICollection<Site> Sites { get; set; } = new List<Site>();
}
