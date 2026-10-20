using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class PropertyContact : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // FyreApp ref (e.g. "PC-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: the Uptick ID if migrated, otherwise the FyreApp ref
    public string? DisplayRef => UptickId?.ToString() ?? FyreRef;

    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;

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
