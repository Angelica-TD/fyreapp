using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.Quotes;

public class CreateQuoteVm
{
    [Required(ErrorMessage = "Client is required.")]
    public int? ClientId { get; set; }

    [Required(ErrorMessage = "Site is required.")]
    public int? SiteId { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public List<CreateQuoteLineItemVm> LineItems { get; set; } = new();
}

public class CreateQuoteLineItemVm
{
    [Required(ErrorMessage = "Asset is required.")]
    public int? AssetId { get; set; }

    [Required(ErrorMessage = "Service type is required.")]
    public int? ServiceTypeId { get; set; }

    [Range(1, 10000, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; } = 1;

    [Range(0, 9_999_999.99, ErrorMessage = "Invalid unit price.")]
    public decimal UnitPrice { get; set; }

    public bool IsRecurring { get; set; }
    public int? RecurringIntervalId { get; set; }
}
