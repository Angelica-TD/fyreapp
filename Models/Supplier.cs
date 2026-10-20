using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A business Fyrepower buys from (Uptick "suppliers")
public class Supplier : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Ref { get; set; }

    // ABN
    [StringLength(50)]
    public string? BusinessNumber { get; set; }

    // Uptick "Partner Terms", e.g. payment terms from the accounting system
    [StringLength(100)]
    public string? PaymentTerms { get; set; }

    [StringLength(50)]
    public string? PaymentDay { get; set; }

    [StringLength(200)]
    public string? AccountsContactName { get; set; }

    [StringLength(200)]
    public string? AccountsEmail { get; set; }

    [StringLength(200)]
    public string? PurchasingContactName { get; set; }

    [StringLength(200)]
    public string? PurchasingEmail { get; set; }

    // e.g. "Fyrepower Fire Protection"
    [StringLength(200)]
    public string? AccountingOrganisation { get; set; }

    public bool Active { get; set; } = true;

    public DateTime? CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }

    // Uptick's terms code with its day, in words, e.g. "14 days after invoice date"
    public string? PaymentTermsLabel => PaymentTerms?.ToUpperInvariant() switch
    {
        null or "" => null,
        "DAYSAFTERDATE" => $"{PaymentDay ?? "?"} days after invoice date",
        "DAYOFFOLLOWINGMONTH" => $"Day {PaymentDay ?? "?"} of the following month",
        _ => PaymentDay == null ? PaymentTerms : $"{PaymentTerms} ({PaymentDay})"
    };
}
