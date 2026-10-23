using System.Globalization;
using FyreApp.Auth;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.ViewModels.Field;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// The technicians' field app, for a phone: their day, a task's details, their time on it and the assets they service.
// A technician works on the tasks assigned to them or that they're booked on; an Admin can open any.
[Authorize]
public class FieldController : Controller
{
    private readonly IFieldService _field;
    private readonly UserManager<ApplicationUser> _users;

    public FieldController(IFieldService field, UserManager<ApplicationUser> users)
    {
        _field = field;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? date, CancellationToken ct = default)
    {
        var day = DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : DateTime.UtcNow.ToSydney().Date;
        var me = await TechnicianIdAsync();
        var vm = me is { } id ? await _field.DayAsync(id, day, ct) : new() { Day = day, NotLinked = true };
        return View(vm);
    }

    [HttpGet("/Field/Task/{id:int}")]
    public async Task<IActionResult> Visit(int id, CancellationToken ct = default)
    {
        var me = await TechnicianIdAsync();
        if (!await MayOpenAsync(id, me, ct)) return Forbid();
        var vm = await _field.TaskAsync(id, me, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Start"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, string type, double? lat, double? lng, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.StartAsync(id, me, type, At(lat, lng), ct));
    }

    [HttpPost("/Field/Task/{id:int}/Finish"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? note, double? lat, double? lng, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.FinishAsync(id, me, At(lat, lng), note, ct));
    }

    [HttpPost("/Field/Task/{id:int}/Record"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(int id, int assetId, string routine, string? result, string? notes, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        var done = await _field.RecordAsync(id, me, assetId, routine, result, notes, ct);
        if (Request.Headers.XRequestedWith == "fetch")
            return done.Outcome == FieldOutcome.Done ? Ok(new { result }) : BadRequest(new { error = done.Message ?? "Not saved." });
        return Done(id, done, $"asset-{assetId}");
    }

    // Photos come straight off a phone (field.js shrinks them first when it can)
    private const long PhotoUploadLimit = 200L * 1024 * 1024;

    [HttpGet("/Field/Task/{id:int}/Remark")]
    public async Task<IActionResult> Remark(int id, int? assetId, [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var vm = await remarks.RaiseFormAsync(id, assetId, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Remark"), ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoUploadLimit), RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadLimit)]
    public async Task<IActionResult> Remark(int id, [Bind(Prefix = "Input")] RaiseRemarkInput input, List<IFormFile> photos,
        [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var saved = await remarks.RaiseAsync(id, input, photos, ct);
        if (saved.Result.Outcome == FieldOutcome.Invalid)
        {
            var vm = await remarks.RaiseFormAsync(id, input.AssetId, ct);
            if (vm == null) return NotFound();
            vm.Input = input;
            ModelState.AddModelError("", saved.Result.Message!);
            return View(vm);
        }
        return Saved(id, saved, "Remark raised", input.AssetId);
    }

    [HttpGet("/Field/Task/{id:int}/Remark/{remarkId:int}/Resolve")]
    public async Task<IActionResult> Resolve(int id, int remarkId, [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var vm = await remarks.ResolveFormAsync(id, remarkId, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Remark/{remarkId:int}/Resolve"), ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoUploadLimit), RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadLimit)]
    public async Task<IActionResult> Resolve(int id, int remarkId, string? resolution, List<IFormFile> photos,
        [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var saved = await remarks.ResolveAsync(id, remarkId, resolution, photos, ct);
        if (saved.Result.Outcome == FieldOutcome.Invalid)
        {
            var vm = await remarks.ResolveFormAsync(id, remarkId, ct);
            if (vm == null) return Done(id, saved.Result);
            vm.Resolution = resolution;
            ModelState.AddModelError("", saved.Result.Message!);
            return View(vm);
        }
        return Saved(id, saved, "Remark resolved", null);
    }

    private IActionResult Saved(int id, RemarkSaved saved, string what, int? assetId)
    {
        if (saved.Result.Outcome != FieldOutcome.Done) return Done(id, saved.Result);
        if (saved.PhotoProblems is { Count: > 0 } problems)
            TempData["Error"] = $"{what}, but not all its photos: {string.Join(" ", problems)}";
        else
            TempData["Success"] = $"{what}.";
        return Redirect(Url.Action(nameof(Visit), new { id }) + (assetId == null ? "" : $"#asset-{assetId}"));
    }

    private IActionResult Done(int id, FieldResult result, string? anchor = null)
    {
        if (result.Outcome == FieldOutcome.NotFound && result.Message == null) return NotFound();
        if (result.Outcome != FieldOutcome.Done) TempData["Error"] = result.Message;
        return Redirect(Url.Action(nameof(Visit), new { id }) + (anchor == null ? "" : "#" + anchor));
    }

    private async Task<bool> MayOpenAsync(int taskId, int? me, CancellationToken ct) =>
        User.IsInRole(DeveloperRoles.Admin) || (me is { } id && await _field.IsTheirsAsync(taskId, id, ct));

    // The Uptick user (technician) the signed-in login is linked to
    private async Task<int?> TechnicianIdAsync() => (await _users.GetUserAsync(User))?.UptickUserId;

    private static Coords? At(double? lat, double? lng) => lat is { } a && lng is { } b ? new Coords(a, b) : null;
}
