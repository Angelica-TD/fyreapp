using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Files;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Field;

public interface IFieldSignOffService
{
    Task<TaskSignOff?> GetAsync(int taskId, CancellationToken ct = default);

    // The customer signs (their name and a signature drawn on the phone), or the technician says why nobody did.
    // Replaces an earlier sign-off on the task.
    Task<FieldResult> SignAsync(int taskId, int technicianId, string? signee, string? signature, string? noSignatureReason, CancellationToken ct = default);
}

// The customer's sign-off on a task in the field app, as Uptick's taskclientsignatures: a signature kept in the file
// store (shown on the task's "Photos and files" panel with the signee's name), or a reason it wasn't signed
public class FieldSignOffService : IFieldSignOffService
{
    private readonly AppDbContext _db;
    private readonly IFileStore _store;

    public FieldSignOffService(AppDbContext db, IFileStore store)
    {
        _db = db;
        _store = store;
    }

    public Task<TaskSignOff?> GetAsync(int taskId, CancellationToken ct = default) =>
        _db.TaskSignOffs.AsNoTracking().Include(s => s.UptickUser).FirstOrDefaultAsync(s => s.ClientTaskId == taskId, ct);

    public async Task<FieldResult> SignAsync(int taskId, int technicianId, string? signee, string? signature, string? noSignatureReason,
        CancellationToken ct = default)
    {
        var task = await _db.ClientTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null) return new(FieldOutcome.NotFound);
        if (!FieldService.IsOpen(task)) return new(FieldOutcome.Invalid, "This task is closed.");

        signee = Clean(signee);
        noSignatureReason = Clean(noSignatureReason);
        var signed = !string.IsNullOrEmpty(signature);
        if (signed && signee == null) return new(FieldOutcome.Invalid, "Enter the name of who signed.");
        if (!signed && noSignatureReason == null) return new(FieldOutcome.Invalid, "Have the customer sign, or say why they didn't.");

        var now = DateTime.UtcNow;
        UptickFile? file = null;
        if (signed)
        {
            var (added, problem) = await FyreAppFiles.AddSignatureAsync(_db, _store, UptickResources.Tasks, taskId, signature!, signee, now, ct);
            if (added == null) return new(FieldOutcome.Invalid, problem);
            file = added;
        }

        // Signing again replaces the earlier sign-off and its signature
        var earlier = await _db.TaskSignOffs.Include(s => s.SignatureFile).FirstOrDefaultAsync(s => s.ClientTaskId == taskId, ct);
        if (earlier != null)
        {
            if (earlier.SignatureFile != null) _db.UptickFiles.Remove(earlier.SignatureFile);
            _db.TaskSignOffs.Remove(earlier);
            await _db.SaveChangesAsync(ct);
        }

        _db.TaskSignOffs.Add(new TaskSignOff
        {
            ClientTaskId = taskId,
            SigneeName = signed ? Cap(signee, 300) : null,
            NoSignatureReason = signed ? null : Cap(noSignatureReason, 1000),
            SignatureFile = file,
            SignedUtc = now,
            UptickUserId = technicianId
        });
        await _db.SaveChangesAsync(ct);
        return FieldResult.Done;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Cap(string? s, int max) => s?.Length > max ? s[..max] : s;
}
