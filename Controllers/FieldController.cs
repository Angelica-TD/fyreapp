using System.Globalization;
using FyreApp.Auth;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.Services.Forms;
using FyreApp.ViewModels.Field;
using Microsoft.AspNetCore.Antiforgery;
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
    public async Task<IActionResult> Visit(int id, [FromServices] IFieldFormService forms, [FromServices] IFieldSignOffService signOffs,
        CancellationToken ct = default)
    {
        var me = await TechnicianIdAsync();
        if (!await MayOpenAsync(id, me, ct)) return Forbid();
        var vm = await _field.TaskAsync(id, me, ct);
        if (vm == null) return NotFound();
        vm.Forms = await forms.TaskFormsAsync(id, ct);
        if (vm.CannotWork == null) vm.FormTemplates = await forms.TemplatesAsync(ct);
        vm.SignOff = await signOffs.GetAsync(id, ct);
        return View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Start"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, string type, double? lat, double? lng, long? doneAt, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.StartAsync(id, me, type, At(lat, lng), When(doneAt), ct));
    }

    [HttpPost("/Field/Task/{id:int}/Finish"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? note, double? lat, double? lng, long? doneAt, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.FinishAsync(id, me, At(lat, lng), note, When(doneAt), ct));
    }

    [HttpPost("/Field/Task/{id:int}/Record"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(int id, int assetId, string routine, string? result, string? notes, long? doneAt, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.RecordAsync(id, me, assetId, routine, result, notes, When(doneAt), ct), $"asset-{assetId}");
    }

    [HttpPost("/Field/Task/{id:int}/Line"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Line(int id, int subtaskId, bool done, long? doneAt, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.SetLineDoneAsync(id, subtaskId, done, When(doneAt), ct), "lines");
    }

    [HttpPost("/Field/Task/{id:int}/Stock"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Stock(int id, int productId, decimal quantity, long? doneAt, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.UseStockAsync(id, me, productId, quantity, When(doneAt), ct), "stock");
    }

    [HttpPost("/Field/Task/{id:int}/Stock/{useId:int}/Remove"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveStock(int id, int useId, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        return Done(id, await _field.RemoveStockAsync(id, me, useId, ct), "stock");
    }

    [HttpGet("/Field/Task/{id:int}/Form")]
    public async Task<IActionResult> Form(int id, string version, [FromServices] IFieldFormService forms, CancellationToken ct = default)
    {
        var me = await TechnicianIdAsync();
        if (!await MayOpenAsync(id, me, ct)) return Forbid();
        var vm = await forms.NewAsync(id, version, me, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Form"), ValidateAntiForgeryToken]
    [ActionName("Form")]
    public async Task<IActionResult> SubmitForm(int id, string version, [FromServices] IFieldFormService forms, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        var saved = await forms.SubmitAsync(id, me, version, Request.Form, ct);
        if (saved.Result.Outcome == FieldOutcome.Invalid && saved.Values != null && !Fetched)
        {
            // Show it again as filled in, with what's missing
            var vm = await forms.NewAsync(id, version, me, ct);
            if (vm == null) return NotFound();
            vm.Values = saved.Values;
            vm.Missing = saved.Missing ?? [];
            ModelState.AddModelError("", saved.Result.Message!);
            return View(vm);
        }
        if (saved.Result.Outcome == FieldOutcome.Done) TempData["Success"] = "Form submitted.";
        return Done(id, saved.Result, "forms");
    }

    [HttpGet("/Field/Task/{id:int}/Form/{responseId:int}")]
    public async Task<IActionResult> SubmittedForm(int id, int responseId, [FromServices] IFieldFormService forms, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var vm = await forms.ViewAsync(id, responseId, ct);
        return vm == null ? NotFound() : View("Form", vm);
    }

    [HttpPost("/Field/Task/{id:int}/SignOff"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOff(int id, string? signee, string? signature, string? noSignatureReason, long? doneAt,
        [FromServices] IFieldSignOffService signOffs, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        var saved = await signOffs.SignAsync(id, me, signee, signature, noSignatureReason, When(doneAt), ct);
        if (saved.Outcome == FieldOutcome.Done) TempData["Success"] = "Sign-off saved.";
        return Done(id, saved, "signoff");
    }

    // The job's done: the task is closed and its report written; back to their day
    [HttpPost("/Field/Task/{id:int}/Complete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, string? note, double? lat, double? lng, long? doneAt, [FromServices] IFieldFinishService finish,
        CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        var done = await finish.CompleteAsync(id, me, note, At(lat, lng), When(doneAt), ct);
        if (done.Result.Outcome != FieldOutcome.Done || Fetched) return Done(id, done.Result);
        TempData["Success"] = "Job completed. Its report is ready for the office.";
        return RedirectToAction(nameof(Index));
    }

    // Photos come straight off a phone (field.js shrinks them first when it can)
    private const long PhotoUploadLimit = 200L * 1024 * 1024;

    // An asset at the job's property: its result on the job, remarks, photos and history
    [HttpGet("/Field/Task/{id:int}/Asset/{assetId:int}")]
    public async Task<IActionResult> Asset(int id, int assetId, [FromServices] IFieldAssetService assets, CancellationToken ct = default)
    {
        var me = await TechnicianIdAsync();
        if (!await MayOpenAsync(id, me, ct)) return Forbid();
        var vm = await assets.AssetAsync(id, assetId, me, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Asset/{assetId:int}/Photos"), ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoUploadLimit), RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadLimit)]
    public async Task<IActionResult> AssetPhotos(int id, int assetId, List<IFormFile> photos, long? doneAt,
        [FromServices] IFieldAssetService assets, CancellationToken ct = default)
    {
        if (await TechnicianIdAsync() is not { } me || !await MayOpenAsync(id, me, ct)) return Forbid();
        var (result, problems) = await assets.AddPhotosAsync(id, assetId, photos, When(doneAt), ct);
        if (result.Outcome == FieldOutcome.Invalid && !Fetched)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Asset), new { id, assetId });
        }
        if (result.Outcome != FieldOutcome.Done || Fetched) return Done(id, result);
        if (problems.Count > 0) TempData["Error"] = $"Not all the photos were kept: {string.Join(" ", problems)}";
        else TempData["Success"] = photos.Count(p => p.Length > 0) == 1 ? "Photo added." : "Photos added.";
        return Redirect(Url.Action(nameof(Asset), new { id, assetId }) + "#photos");
    }

    [HttpGet("/Field/Task/{id:int}/Remark")]
    public async Task<IActionResult> Remark(int id, int? assetId, int? backToAsset, [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var vm = await remarks.RaiseFormAsync(id, assetId, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Remark"), ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoUploadLimit), RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadLimit)]
    public async Task<IActionResult> Remark(int id, [Bind(Prefix = "Input")] RaiseRemarkInput input, List<IFormFile> photos, long? doneAt, int? backToAsset,
        [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        var me = await TechnicianIdAsync();
        if (!await MayOpenAsync(id, me, ct)) return Forbid();
        var saved = await remarks.RaiseAsync(id, input, photos, me, When(doneAt), ct);
        if (saved.Result.Outcome == FieldOutcome.Invalid && !Fetched)
        {
            var vm = await remarks.RaiseFormAsync(id, input.AssetId, ct);
            if (vm == null) return NotFound();
            vm.Input = input;
            ModelState.AddModelError("", saved.Result.Message!);
            return View(vm);
        }
        return Saved(id, saved, "Remark raised", input.AssetId, backToAsset);
    }

    [HttpGet("/Field/Task/{id:int}/Remark/{remarkId:int}/Resolve")]
    public async Task<IActionResult> Resolve(int id, int remarkId, int? backToAsset, [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        if (!await MayOpenAsync(id, await TechnicianIdAsync(), ct)) return Forbid();
        var vm = await remarks.ResolveFormAsync(id, remarkId, ct);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost("/Field/Task/{id:int}/Remark/{remarkId:int}/Resolve"), ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoUploadLimit), RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadLimit)]
    public async Task<IActionResult> Resolve(int id, int remarkId, string? resolution, List<IFormFile> photos, long? doneAt, int? backToAsset,
        [FromServices] IFieldRemarkService remarks, CancellationToken ct = default)
    {
        var me = await TechnicianIdAsync();
        if (!await MayOpenAsync(id, me, ct)) return Forbid();
        var saved = await remarks.ResolveAsync(id, remarkId, resolution, photos, me, When(doneAt), ct);
        if (saved.Result.Outcome == FieldOutcome.Invalid && !Fetched)
        {
            var vm = await remarks.ResolveFormAsync(id, remarkId, ct);
            if (vm == null) return Done(id, saved.Result);
            vm.Resolution = resolution;
            ModelState.AddModelError("", saved.Result.Message!);
            return View(vm);
        }
        return Saved(id, saved, "Remark resolved", null, backToAsset);
    }

    private IActionResult Saved(int id, RemarkSaved saved, string what, int? assetId, int? backToAsset)
    {
        if (saved.Result.Outcome != FieldOutcome.Done || Fetched) return Done(id, saved.Result);
        if (saved.PhotoProblems is { Count: > 0 } problems)
            TempData["Error"] = $"{what}, but not all its photos: {string.Join(" ", problems)}";
        else
            TempData["Success"] = $"{what}.";
        // Back where it was raised from: the asset's page, or the job at the asset
        if (backToAsset is { } back) return RedirectToAction(nameof(Asset), new { id, assetId = back });
        return Redirect(Url.Action(nameof(Visit), new { id }) + (assetId == null ? "" : $"#asset-{assetId}"));
    }

    // A fresh anti-forgery token for sending changes held on the phone (the one on the page they came from may be for a
    // cookie that's gone since)
    [HttpGet("/Field/Token")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Token([FromServices] IAntiforgery antiforgery) =>
        Json(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    // Sent by the phone itself (a save as it changes, or a change held on the phone while there was no signal):
    // answered in JSON rather than with a page
    private bool Fetched => Request.Headers.XRequestedWith == "fetch";

    private IActionResult Done(int id, FieldResult result, string? anchor = null)
    {
        if (Fetched)
            return result.Outcome switch
            {
                FieldOutcome.Done => Ok(new { saved = true }),
                FieldOutcome.NotFound => NotFound(new { error = result.Message ?? "Not found." }),
                FieldOutcome.NotAllowed => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Message ?? "Not allowed." }),
                _ => BadRequest(new { error = result.Message ?? "Not saved." })
            };
        if (result.Outcome == FieldOutcome.NotFound && result.Message == null) return NotFound();
        // An error shows at the top of the page, so go there rather than to the section
        if (result.Outcome != FieldOutcome.Done) (TempData["Error"], anchor) = (result.Message, null);
        return Redirect(Url.Action(nameof(Visit), new { id }) + (anchor == null ? "" : "#" + anchor));
    }

    private async Task<bool> MayOpenAsync(int taskId, int? me, CancellationToken ct) =>
        User.IsInRole(DeveloperRoles.Admin) || (me is { } id && await _field.IsTheirsAsync(taskId, id, ct));

    // The Uptick user (technician) the signed-in login is linked to
    private async Task<int?> TechnicianIdAsync() => (await _users.GetUserAsync(User))?.UptickUserId;

    private static Coords? At(double? lat, double? lng) => lat is { } a && lng is { } b ? new Coords(a, b) : null;

    private static DateTime? When(long? doneAt) => FieldService.WhenDone(doneAt, DateTime.UtcNow);
}
