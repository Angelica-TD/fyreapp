using FyreApp.Models;

namespace FyreApp.Services.Quotes;

// ── Create ──────────────────────────────────────────────────────────────────

public enum QuoteCreateStatus { Success, ValidationError, ClientNotFound, SiteNotFound }
public record QuoteCreateResult(QuoteCreateStatus Status, Quote? Quote = null, string? Error = null);

// ── Update ──────────────────────────────────────────────────────────────────

public enum QuoteUpdateStatus { Success, NotFound, ValidationError }
public record QuoteUpdateResult(QuoteUpdateStatus Status, string? Error = null);

// ── Accept ───────────────────────────────────────────────────────────────────

public enum QuoteAcceptStatus { Success, NotFound, AlreadyAccepted, InvalidState }
public record QuoteAcceptResult(QuoteAcceptStatus Status, string? Error = null);

// ── Delete ───────────────────────────────────────────────────────────────────

public enum QuoteDeleteStatus { Success, NotFound }
public record QuoteDeleteResult(QuoteDeleteStatus Status);

// ── List ─────────────────────────────────────────────────────────────────────

public class QuoteListItemVm
{
    public int Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; }
    public int LineItemCount { get; set; }
    public decimal Total { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
