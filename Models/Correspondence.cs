using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// An email in Uptick ("correspondence"): one Uptick sent (a report, an invoice, a quote reminder…) or one it took in
// (a customer's reply to admin@). Its links say which records it's about (clients, properties, tasks, invoices…).
public class Correspondence : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    [StringLength(1000)] public string? Subject { get; set; }

    // As Uptick holds them: "Fyrepower Fire Protection via Uptick <no-reply@…>", "jo@eip.edu.au, …"
    [StringLength(500)] public string? From { get; set; }
    [StringLength(300)] public string? FromName { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    [StringLength(500)] public string? ReplyTo { get; set; }

    public DateTime? SentUtc { get; set; }

    // The Uptick user who sent it, when one did (most are sent by Uptick itself)
    public int? SentById { get; set; }
    public UptickUser? SentBy { get; set; }

    public bool IsStarred { get; set; }

    // The email as sent. Shown only inside a sandboxed frame (Views/Correspondence/Details).
    public string? BodyHtml { get; set; }

    // Its attachments: file name and where Uptick keeps it ("reports/<id>/…", "invoices/<id>/…", a document's path)
    public List<EmailAttachment> Attachments { get; set; } = [];

    public List<CorrespondenceLink> Links { get; set; } = [];
}

public sealed record EmailAttachment(string Name, string? Path);

// A record an email is about ("correspondenceobjectlinks"): of which kind (as an Uptick endpoint name: "clients",
// "properties", "tasks", "invoices", "defectquotes", "dispatches", …) and its Uptick id, and its name in Uptick
// ("Q-0057", "Dpro"). Like UptickFile, it points by Uptick id, so a page finds its emails with the id it already has.
public class CorrespondenceLink : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    public int CorrespondenceId { get; set; }
    public Correspondence Correspondence { get; set; } = null!;

    [StringLength(50)] public string RecordType { get; set; } = string.Empty;
    public long RecordUptickId { get; set; }
    [StringLength(300)] public string? Name { get; set; }
}

// Uptick's "dispatch" (its issuance): a task's invoice and reports sent to the customer, with how it went
// ("SENT", "FAILED") and each email it sent
public class Dispatch : IUptickMapped
{
    public int Id { get; set; }
    public long? UptickId { get; set; }

    // "DIS-52042"
    [StringLength(50)] public string? Ref { get; set; }
    [StringLength(30)] public string? Status { get; set; }

    // "52042: PM2026/10 Servicing - Portables & Fire Equipment"
    [StringLength(500)] public string? Title { get; set; }

    public int? ClientTaskId { get; set; }
    public ClientTask? ClientTask { get; set; }

    public int? PropertyId { get; set; }
    public Property? Property { get; set; }

    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int? SenderId { get; set; }
    public UptickUser? Sender { get; set; }

    // Whether sending it issued the invoice
    public bool IssueInvoice { get; set; }

    public DateTime? CreatedUtc { get; set; }

    public List<DispatchEmail> Emails { get; set; } = [];
}

public sealed record DispatchEmail(
    IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc, string? From, string? ReplyTo,
    string? Subject, string? Attention, string? BodyHtml, IReadOnlyList<EmailAttachment> Attachments);
