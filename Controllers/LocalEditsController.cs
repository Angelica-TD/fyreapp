using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Controllers;

// Puts a field edited in FyreApp back to Uptick's value (docs/uptick-migration.md §7)
[Authorize(Roles = "Admin")]
public class LocalEditsController : Controller
{
    private static readonly Dictionary<string, Type> Entities = new[]
    {
        typeof(Client), typeof(Property), typeof(PropertyContact), typeof(ClientContact), typeof(UptickUser),
        typeof(Asset), typeof(AssetType), typeof(AssetTypeVariant), typeof(RoutineServiceType), typeof(RoutineServiceLevel),
        typeof(RemarkType), typeof(ClientTask), typeof(ServiceTask)
    }.ToDictionary(t => t.Name);

    private readonly AppDbContext _db;

    public LocalEditsController(AppDbContext db) => _db = db;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(int id, string? returnUrl, CancellationToken ct)
    {
        var edit = await _db.LocalEdits.FirstOrDefaultAsync(e => e.Id == id && e.ClosedUtc == null, ct);
        if (edit != null && Entities.TryGetValue(edit.Entity, out var type) && await _db.FindAsync(type, [edit.RecordId], ct) is { } record)
        {
            if (record is Asset asset) await _db.Entry(asset).Collection(a => a.AssetTypes).LoadAsync(ct);
            LocalEditLog.Reset(_db, edit, record, User.Identity?.Name, DateTime.UtcNow);
            // Uptick's value, not a new FyreApp edit
            _db.RecordLocalEdits = false;
            await _db.SaveChangesAsync(ct);
            TempData["Success"] = "Put back to Uptick's value.";
        }
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
    }
}
