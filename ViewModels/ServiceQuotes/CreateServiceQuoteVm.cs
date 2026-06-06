using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class CreateServiceQuoteVm
{
    // Null when creating a new client inline
    public int? ClientId { get; set; }

    // Required — the property this quote is for
    public int? SiteId { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "Description cannot exceed 4000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Service is required.")]
    public int? ServiceOfferingId { get; set; }

    public ServiceQuoteType QuoteType { get; set; } = ServiceQuoteType.OneTime;

    public int? MaintenanceIntervalId { get; set; }

    [Range(0, 9_999_999.99, ErrorMessage = "Amount must be between 0 and 9,999,999.99.")]
    public decimal Amount { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [StringLength(4000, ErrorMessage = "Notes cannot exceed 4000 characters.")]
    public string? Notes { get; set; }
}
