using FyreApp.Data;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Files;

// Links pages make to files copied from Uptick (served by FilesController)
public static class UptickFileLinks
{
    // The held PDFs of records (reports) by Uptick id: the file's id
    public static async Task<Dictionary<long, int>> PdfsAsync(AppDbContext db, string resource, IEnumerable<long?> uptickIds,
        CancellationToken ct = default)
    {
        var ids = uptickIds.OfType<long>().Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, int>();
        return (await db.UptickFiles.AsNoTracking()
                .Where(f => f.RecordType == resource && f.Kind == UptickFilePaths.Pdf && f.StorageKey != null
                            && f.RecordUptickId != null && ids.Contains(f.RecordUptickId.Value))
                .Select(f => new { Id = f.RecordUptickId!.Value, File = f.Id })
                .ToListAsync(ct))
            .GroupBy(f => f.Id)
            .ToDictionary(g => g.Key, g => g.First().File);
    }
}
