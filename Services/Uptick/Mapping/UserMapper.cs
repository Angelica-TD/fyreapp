using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.Services.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's users -> UptickUser, then FyreApp logins linked to them take their name and email, as after a users
// export. The API has no security groups or branches for a user, so those (from the export) are kept.
public class UserMapper : UptickEntityMapper<UptickUser>
{
    // Service group id -> name
    private Dictionary<long, string> _serviceGroups = new();

    public UserMapper(AppDbContext db) : base(db) { }

    public override string Resource => "users";

    public override async Task BeginAsync(CancellationToken ct)
    {
        var groups = await Db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == "servicegroups" && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.Attributes })
            .ToListAsync(ct);
        _serviceGroups = new Dictionary<long, string>();
        foreach (var g in groups)
        {
            using var a = System.Text.Json.JsonDocument.Parse(g.Attributes);
            if (long.TryParse(g.UptickId, out var id) && UptickRaw.Str(a.RootElement, "name")?.Trim() is { Length: > 0 } name)
                _serviceGroups[id] = name;
        }
    }

    protected override async Task<Dictionary<long, UptickUser>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var texts = ids.Select(id => id.ToString()).ToList();
        var rows = await Db.UptickUsers
            .Where(u => (u.UptickId != null && ids.Contains(u.UptickId.Value)) || (u.ExternalId != null && texts.Contains(u.ExternalId)))
            .ToListAsync(ct);
        return Match(rows, batch.Select(r => (r.Id, (string?)r.Id.ToString())), u => u.UptickId, u => u.ExternalId);
    }

    protected override UptickUser Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(UptickUser u, UptickRaw raw)
    {
        u.UptickId = raw.Id;
        u.ExternalId = raw.Id.ToString();
        u.Name = raw.Text("name", 200) ?? $"User {raw.Id}";
        u.Email = raw.Text("email", 200);
        u.LicenseType = raw.Text("license", 20)?.ToUpperInvariant();
        // The trade licence, e.g. "QBCC 15366855"
        var licence = UptickRaw.Str(UptickRaw.Get(raw.A, "extra_fields"), "license")?.Trim();
        u.Licence = string.IsNullOrEmpty(licence) ? null : licence.Length > 200 ? licence[..200] : licence;
        u.ServiceGroup = raw.Rel("servicegroup") is { } group ? _serviceGroups.GetValueOrDefault(group) : null;
        u.CostRate = raw.Decimal("rate");
        u.SellRate = raw.Decimal("sell_rate");
        u.JoinedUtc = raw.Time("date_joined");
        u.LastLoginUtc = raw.Time("last_login");
    }

    // A user gone from Uptick stays: tasks and logins point at it
    protected override void Deactivate(UptickUser u) { }

    protected override async Task AfterAsync(CancellationToken ct)
    {
        await UptickLoginSync.ApplyAsync(Db, ct);
        await TechnicianMatch.AssignImportedTasksAsync(Db, ct);
        await Db.SaveChangesAsync(ct);
    }
}
