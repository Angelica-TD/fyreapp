using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class SiteContact
{
    public int Id { get; set; }

    // Maps from Uptick property contact export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "PC-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: the Uptick ID if imported, otherwise the FyreApp ref
    public string? DisplayRef => string.IsNullOrWhiteSpace(ExternalId) ? FyreRef : ExternalId;

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Organisation { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? BusinessHours { get; set; }

    // Uptick role, e.g. "propertymanager", "accesscontact"
    public string? Role { get; set; }

    // Which documents/notifications this contact receives
    public bool InvoiceRequired { get; set; }
    public bool ReportRequired { get; set; }
    public bool QuoteRequired { get; set; }
    public bool NotificationRequired { get; set; }

    public bool Active { get; set; } = true;
}
