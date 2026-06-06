using System.ComponentModel.DataAnnotations;
using FyreApp.Models;

namespace FyreApp.ViewModels.Quotes;

public class EditQuoteVm
{
    public QuoteStatus Status { get; set; }
    public DateTime? ExpiryDate { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}
