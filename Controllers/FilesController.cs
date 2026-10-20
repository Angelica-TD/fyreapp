using FyreApp.Data;
using FyreApp.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace FyreApp.Controllers;

// The files copied from Uptick (docs/uptick-migration.md §8), from FyreApp's file store: shown in the browser
// (photos, PDFs), or saved with ?download=true. Signed-in staff only; the store itself is private.
[Authorize]
public class FilesController : Controller
{
    // Shown in the browser; anything else (an uploaded HTML or SVG file, say) is only ever downloaded
    private static readonly HashSet<string> Inline = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp", "application/pdf"
    };

    private readonly AppDbContext _db;
    private readonly IFileStore _store;

    public FilesController(AppDbContext db, IFileStore store)
    {
        _db = db;
        _store = store;
    }

    [HttpGet("Files/{id:int}")]
    public async Task<IActionResult> Get(int id, bool download = false, CancellationToken ct = default)
    {
        var file = await _db.UptickFiles.AsNoTracking()
            .Where(f => f.Id == id && f.StorageKey != null)
            .Select(f => new { f.StorageKey, f.Path, f.ContentType, f.Md5 })
            .FirstOrDefaultAsync(ct);
        if (file == null) return NotFound();

        // A stored file never changes under its id until Uptick's does (then its MD5 does)
        var etag = file.Md5 == null ? null : new EntityTagHeaderValue($"\"{file.Md5}\"");
        if (etag != null && Request.Headers.IfNoneMatch.ToString().Contains(etag.Tag.ToString(), StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status304NotModified);

        var stream = await _store.OpenReadAsync(file.StorageKey!, ct);
        if (stream == null) return NotFound();

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        var name = Path.GetFileName(file.Path ?? file.StorageKey!);
        var type = file.ContentType ?? "application/octet-stream";
        if (download || !Inline.Contains(type)) return File(stream, type, name, lastModified: null, entityTag: etag);

        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = name }.ToString();
        return File(stream, type, lastModified: null, entityTag: etag);
    }
}
