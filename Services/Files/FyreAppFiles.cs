using System.Security.Cryptography;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Uptick.Files;
using Microsoft.AspNetCore.Http;

namespace FyreApp.Services.Files;

// Files added in FyreApp (photos taken in the field), kept in the file store and listed as UptickFile rows with
// Source "fyreapp" and the FyreApp record they belong to, so they show and are served like Uptick's (/Files/{id})
public static class FyreAppFiles
{
    public const string Source = "fyreapp";

    // A remark's photo as found, and once fixed (Uptick's photo types)
    public const string Open = "open";
    public const string Fixed = "fixed";

    public const long MaxPhotoBytes = 20 * 1024 * 1024;

    private static readonly Dictionary<string, string> PhotoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/heic"] = ".heic", ["image/heif"] = ".heif"
    };

    // Stores the photos for the record (not saved yet: the caller saves the context). Returns why any were left out.
    public static async Task<IReadOnlyList<string>> AddPhotosAsync(
        AppDbContext db, IFileStore store, string recordType, int recordId, string photoType,
        IEnumerable<IFormFile> photos, DateTime now, CancellationToken ct)
    {
        var problems = new List<string>();
        var list = photos.Where(p => p.Length > 0).ToList();
        if (list.Count == 0) return problems;
        if (!store.IsConfigured) return ["Photos weren't kept: file storage isn't set up."];

        foreach (var photo in list)
        {
            if (!PhotoTypes.TryGetValue(photo.ContentType ?? "", out var ext))
            {
                problems.Add($"{photo.FileName} isn't a photo (JPEG, PNG, WebP or HEIC).");
                continue;
            }
            if (photo.Length > MaxPhotoBytes)
            {
                problems.Add($"{photo.FileName} is over {MaxPhotoBytes / 1024 / 1024} MB.");
                continue;
            }

            var id = Guid.NewGuid().ToString("N");
            var key = $"fyreapp/{recordType}/{recordId}/{id}{ext}";
            using var buffer = new MemoryStream();
            await photo.CopyToAsync(buffer, ct);
            var md5 = Convert.ToHexStringLower(MD5.HashData(buffer.ToArray()));
            buffer.Position = 0;
            try
            {
                await store.PutAsync(key, buffer, photo.ContentType, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problems.Add($"{photo.FileName} couldn't be stored: {ex.Message}");
                continue;
            }

            db.UptickFiles.Add(new UptickFile
            {
                Source = Source,
                SourceId = id,
                Path = $"fyreapp/{recordType}/{recordId}/{SafeName(photo.FileName, id + ext)}",
                RecordType = recordType,
                RecordId = recordId,
                Kind = UptickFilePaths.Photo,
                PhotoType = photoType,
                CreatedUtc = now,
                Size = buffer.Length,
                ContentType = photo.ContentType,
                StorageKey = key,
                Md5 = md5,
                DownloadedUtc = now,
                CheckedUtc = now
            });
        }
        return problems;
    }

    // A signature drawn on the phone, sent as a PNG data: URL. Returns the file row (not saved yet), or why not.
    public static async Task<(UptickFile? File, string? Problem)> AddSignatureAsync(
        AppDbContext db, IFileStore store, string recordType, int recordId, string dataUrl, string? caption, DateTime now, CancellationToken ct)
    {
        const string prefix = "data:image/png;base64,";
        if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal)) return (null, "The signature didn't come through. Sign again.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[prefix.Length..]);
        }
        catch (FormatException)
        {
            return (null, "The signature didn't come through. Sign again.");
        }
        // A PNG starts with these bytes; anything else (or huge) isn't a signature from the pad
        if (bytes.Length is < 8 or > 2 * 1024 * 1024 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
            return (null, "The signature didn't come through. Sign again.");
        if (!store.IsConfigured) return (null, "The signature can't be kept: file storage isn't set up.");

        var id = Guid.NewGuid().ToString("N");
        var key = $"fyreapp/{recordType}/{recordId}/{id}.png";
        try
        {
            await store.PutAsync(key, new MemoryStream(bytes), "image/png", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"The signature couldn't be stored: {ex.Message}");
        }

        var file = new UptickFile
        {
            Source = Source,
            SourceId = id,
            Path = $"fyreapp/{recordType}/{recordId}/signature-{now:yyyyMMddHHmmss}.png",
            RecordType = recordType,
            RecordId = recordId,
            Kind = UptickFilePaths.Signature,
            Caption = caption?.Length > 300 ? caption[..300] : caption,
            CreatedUtc = now,
            Size = bytes.Length,
            ContentType = "image/png",
            StorageKey = key,
            Md5 = Convert.ToHexStringLower(MD5.HashData(bytes)),
            DownloadedUtc = now,
            CheckedUtc = now
        };
        db.UptickFiles.Add(file);
        return (file, null);
    }

    // The name the phone gave it, without any folders
    private static string SafeName(string? name, string fallback)
    {
        var n = Path.GetFileName(name ?? "").Trim();
        return n.Length is > 0 and <= 200 ? n : fallback;
    }
}
