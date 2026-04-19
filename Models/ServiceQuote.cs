using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

public enum ServiceQuoteStatus
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Declined = 4,
    Expired = 5
}

public enum ServiceQuoteType
{
    [Display(Name = "One-Time")]
    OneTime = 1,

    [Display(Name = "Routine / Contract")]
    Routine = 2
}

public class ServiceQuote
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    [Required, StringLength(20)]
    public string QuoteNumber { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public ServiceQuoteStatus Status { get; set; } = ServiceQuoteStatus.Draft;
    public ServiceQuoteType QuoteType { get; set; } = ServiceQuoteType.OneTime;

    [Range(0, 9_999_999.99)]
    public decimal Amount { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime? ExpiryDate { get; set; }

    // Set when quote is sent to client — used as the public view token
    public Guid? ClientToken { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
}
