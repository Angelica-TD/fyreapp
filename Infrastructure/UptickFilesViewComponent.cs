using FyreApp.Data;
using FyreApp.Services.Uptick.Files;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Infrastructure;

// "Photos and files": what Uptick stores for a migrated record (docs/uptick-migration.md §8), as copied into FyreApp's
// file store: photos (a remark's as found and once fixed), customers' signatures, attachments and PDFs. Files listed
// but not copied yet are counted.
public class UptickFilesViewComponent : ViewComponent
{
    public sealed record Item(int Id, string Name, string Kind, string? PhotoType, string? Caption, long? Size, DateTime? CreatedUtc);

    public sealed record Model(IReadOnlyList<Item> Photos, IReadOnlyList<Item> Signatures, IReadOnlyList<Item> Documents, int Waiting)
    {
        public bool Any => Photos.Count + Signatures.Count + Documents.Count + Waiting > 0;
    }

    private readonly AppDbContext _db;

    public UptickFilesViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync(string resource, long? uptickId)
    {
        if (uptickId is not { } id) return Content("");
        var model = await LoadAsync(_db, resource, id);
        return model.Any ? View(model) : Content("");
    }

    public static async Task<Model> LoadAsync(AppDbContext db, string resource, long uptickId)
    {
        var rows = await db.UptickFiles.AsNoTracking()
            .Where(f => f.RecordType == resource && f.RecordUptickId == uptickId && !f.NoFile)
            .Select(f => new { f.Id, f.Path, f.Kind, f.PhotoType, f.Caption, f.Size, f.CreatedUtc, Held = f.StorageKey != null })
            .ToListAsync();
        var held = rows.Where(f => f.Held)
            .OrderBy(f => f.CreatedUtc ?? DateTime.MaxValue).ThenBy(f => f.Id)
            .Select(f => new Item(f.Id, System.IO.Path.GetFileName(f.Path ?? "") is { Length: > 0 } name ? name : "Report PDF",
                f.Kind ?? UptickFilePaths.Document, f.PhotoType, f.Caption, f.Size, f.CreatedUtc))
            .ToList();
        return new Model(
            held.Where(f => f.Kind == UptickFilePaths.Photo).ToList(),
            held.Where(f => f.Kind == UptickFilePaths.Signature).ToList(),
            held.Where(f => f.Kind is not (UptickFilePaths.Photo or UptickFilePaths.Signature)).ToList(),
            rows.Count(f => !f.Held));
    }
}
