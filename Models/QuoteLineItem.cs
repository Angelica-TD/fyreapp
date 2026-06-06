using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public class QuoteLineItem
{
    public int Id { get; set; }

    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public int ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;

    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;

    [Range(0, 9_999_999.99)]
    public decimal UnitPrice { get; set; }

    public bool IsRecurring { get; set; }

    public int? RecurringIntervalId { get; set; }
    public MaintenanceInterval? RecurringInterval { get; set; }
}
