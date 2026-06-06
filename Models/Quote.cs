using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public enum QuoteStatus
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Declined = 4
}

public class Quote
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string QuoteNumber { get; set; } = string.Empty;

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public DateTime? ExpiryDate { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedBy { get; set; } = null!;

    public ICollection<QuoteLineItem> LineItems { get; set; } = new List<QuoteLineItem>();
}
