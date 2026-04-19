using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class UpdateServiceQuoteRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "Description cannot exceed 4000 characters.")]
    public string? Description { get; set; }

    public ServiceQuoteStatus Status { get; set; }
    public ServiceQuoteType QuoteType { get; set; }

    public int? MaintenanceIntervalId { get; set; }

    [Range(0, 9_999_999.99, ErrorMessage = "Amount must be between 0 and 9,999,999.99.")]
    public decimal Amount { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [StringLength(4000, ErrorMessage = "Notes cannot exceed 4000 characters.")]
    public string? Notes { get; set; }
}
