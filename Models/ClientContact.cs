using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A contact at a client (Uptick "Client contacts" export), beside the client's primary contact
public class ClientContact : IUptickMapped
{
    public int Id { get; set; }

    // Uptick's id, for records migrated through its API
    public long? UptickId { get; set; }

    // FyreApp ref (e.g. "CC-1001"), assigned by the database when there's no Uptick ID
    [StringLength(20)]
    public string? FyreRef { get; set; }

    // Shown to users: the Uptick ID if migrated, otherwise the FyreApp ref
    public string? DisplayRef => UptickId?.ToString() ?? FyreRef;

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
