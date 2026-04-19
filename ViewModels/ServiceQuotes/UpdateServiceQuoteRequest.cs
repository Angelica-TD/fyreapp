using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.ServiceQuotes;

public class UpdateServiceQuoteRequest
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public ServiceQuoteStatus Status { get; set; }

    [Range(0, 9_999_999.99)]
    public decimal Amount { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}
