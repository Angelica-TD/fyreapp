using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A contact at a client (Uptick "Client contacts" export), beside the client's primary contact
public class ClientContact : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // Maps from Uptick client contacts export "ID"
    [StringLength(64)]
    public string? ExternalId { get; set; }

    // FyreApp ref (e.g. "CC-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Every column of the Uptick export row as JSON (key order kept), so nothing is lost on import
    public string? UptickData { get; set; }

    // Shown to users: the Uptick ID if imported, otherwise the FyreApp ref
    public string? DisplayRef => string.IsNullOrWhiteSpace(ExternalId) ? FyreRef : ExternalId;

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(100)]
    public string? Mobile { get; set; }

    [StringLength(100)]
    public string? Phone { get; set; }

    // e.g. "Knight Frank Officer"
    [StringLength(200)]
    public string? Position { get; set; }

    public bool Active { get; set; } = true;
}
