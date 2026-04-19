using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Dev;

namespace FyreApp.Controllers;

[Authorize(Roles = "Developer")]
public class DevController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public DevController(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new DevIndexVm
        {
            Clients = await _db.Clients
                .OrderBy(c => c.Name)
                .Select(c => new DevClientRow(
                    c.Id, c.Name,
                    c.Sites.Count,
                    _db.ClientTasks.Count(t => t.ClientId == c.Id)))
                .ToListAsync(),

            Sites = await _db.Sites
                .OrderBy(s => s.Client.Name).ThenBy(s => s.Name)
                .Select(s => new DevSiteRow(
                    s.Id, s.Name, s.Client.Name,
                    s.AddressDisplay ?? s.AddressLine1,
                    s.Assets.Count))
                .ToListAsync(),

            Assets = await _db.Assets
                .OrderBy(a => a.Site.Client.Name).ThenBy(a => a.Site.Name).ThenBy(a => a.Name)
                .Select(a => new DevAssetRow(a.Id, a.Name, a.Site.Name, a.Site.Client.Name))
                .ToListAsync(),

            Tasks = await _db.ClientTasks
                .OrderBy(t => t.Client.Name).ThenBy(t => t.Title)
                .Select(t => new DevTaskRow(
                    t.Id, t.Title, t.Client.Name,
                    t.Site != null ? t.Site.Name : null,
                    t.Status.ToString()))
                .ToListAsync(),

            Catalogue = await _db.AssetCatalogue
                .OrderBy(a => a.Name)
                .Select(a => new DevCatalogueRow(a.Id, a.Name))
                .ToListAsync(),

            Techs = await (
                from u in _db.Users
                join ur in _db.UserRoles on u.Id equals ur.UserId
                join r in _db.Roles on ur.RoleId equals r.Id
                where r.Name == "Tech"
                orderby u.LastName, u.FirstName
                select new DevTechRow(u.Id, u.FirstName + " " + u.LastName, u.Email!, u.IsActive)
            ).ToListAsync(),

            Admins = await (
                from u in _db.Users
                join ur in _db.UserRoles on u.Id equals ur.UserId
                join r in _db.Roles on ur.RoleId equals r.Id
                where r.Name == "Admin"
                orderby u.LastName, u.FirstName
                select new DevTechRow(u.Id, u.FirstName + " " + u.LastName, u.Email!, u.IsActive)
            ).ToListAsync(),
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string entityType, int[] ids, CancellationToken ct)
    {
        if (ids is null || ids.Length == 0)
        {
            TempData["Info"] = "Nothing selected.";
            return RedirectToAction(nameof(Index));
        }

        int deleted = 0;

        switch (entityType)
        {
            case "Client":
                deleted = await _db.Clients
                    .Where(c => ids.Contains(c.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            case "Site":
                deleted = await _db.Sites
                    .Where(s => ids.Contains(s.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            case "Asset":
                deleted = await _db.Assets
                    .Where(a => ids.Contains(a.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            case "Task":
                deleted = await _db.ClientTasks
                    .Where(t => ids.Contains(t.Id))
                    .ExecuteDeleteAsync(ct);
                break;

            default:
                TempData["Error"] = $"Unknown entity type: {entityType}";
                return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"Deleted {deleted} {entityType}(s).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteUsers(string role, string[] ids)
    {
        if (ids is null || ids.Length == 0)
        {
            TempData["Info"] = "Nothing selected.";
            return RedirectToAction(nameof(Index));
        }

        int deleted = 0;
        foreach (var id in ids)
        {
            var user = await _users.FindByIdAsync(id);
            if (user is null) continue;
            var result = await _users.DeleteAsync(user);
            if (result.Succeeded) deleted++;
        }

        TempData["Success"] = $"Deleted {deleted} {role}(s).";
        return RedirectToAction(nameof(Index));
    }

    // ── Detail partial views (GET) ──

    [HttpGet]
    public async Task<IActionResult> ClientDetail(int id)
    {
        var c = await _db.Clients.FindAsync(id);
        return c is null ? NotFound() : PartialView("_ClientDetail", c);
    }

    [HttpGet]
    public async Task<IActionResult> SiteDetail(int id)
    {
        var s = await _db.Sites.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id);
        return s is null ? NotFound() : PartialView("_SiteDetail", s);
    }

    [HttpGet]
    public async Task<IActionResult> AssetDetail(int id)
    {
        var a = await _db.Assets
            .Include(x => x.Site).ThenInclude(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == id);
        return a is null ? NotFound() : PartialView("_AssetDetail", a);
    }

    [HttpGet]
    public async Task<IActionResult> TaskDetail(int id)
    {
        var t = await _db.ClientTasks
            .Include(x => x.Client)
            .Include(x => x.Site)
            .FirstOrDefaultAsync(x => x.Id == id);
        return t is null ? NotFound() : PartialView("_TaskDetail", t);
    }

    [HttpGet]
    public async Task<IActionResult> UserDetail(string id)
    {
        var u = await _users.FindByIdAsync(id);
        return u is null ? NotFound() : PartialView("_UserDetail", u);
    }

    // ── Update actions (POST) ──

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClientUpdate(DevClientUpdateVm vm, CancellationToken ct)
    {
        var c = await _db.Clients.FindAsync(vm.Id);
        if (c is null) return NotFound();
        c.Name = vm.Name.Trim();
        c.ExternalId = vm.ExternalId?.Trim();
        c.Active = vm.Active;
        c.PrimaryContactName = vm.PrimaryContactName?.Trim();
        c.PrimaryContactEmail = vm.PrimaryContactEmail?.Trim();
        c.PrimaryContactMobile = vm.PrimaryContactMobile?.Trim();
        c.PrimaryContactCcEmail = vm.PrimaryContactCcEmail?.Trim();
        c.PrimaryContactAddress = vm.PrimaryContactAddress?.Trim();
        c.BillingName = vm.BillingName?.Trim();
        c.BillingAttentionTo = vm.BillingAttentionTo?.Trim();
        c.BillingEmail = vm.BillingEmail?.Trim();
        c.BillingCcEmail = vm.BillingCcEmail?.Trim();
        c.BillingAddress = vm.BillingAddress?.Trim();
        c.Updated = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Client '{c.Name}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SiteUpdate(DevSiteUpdateVm vm, CancellationToken ct)
    {
        var s = await _db.Sites.FindAsync(vm.Id);
        if (s is null) return NotFound();
        s.Name = vm.Name.Trim();
        s.AddressDisplay = vm.AddressDisplay?.Trim();
        s.AddressLine1 = vm.AddressLine1?.Trim();
        s.AddressLine2 = vm.AddressLine2?.Trim();
        s.Suburb = vm.Suburb?.Trim();
        s.Postcode = vm.Postcode?.Trim();
        s.State = vm.State?.Trim();
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Site '{s.Name}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssetUpdate(DevAssetUpdateVm vm, CancellationToken ct)
    {
        var a = await _db.Assets.FindAsync(vm.Id);
        if (a is null) return NotFound();
        a.Name = vm.Name.Trim();
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Asset '{a.Name}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TaskUpdate(DevTaskUpdateVm vm, CancellationToken ct)
    {
        var t = await _db.ClientTasks.FindAsync(vm.Id);
        if (t is null) return NotFound();
        t.Title = vm.Title.Trim();
        t.Description = vm.Description?.Trim();
        t.Status = vm.Status;
        t.Priority = vm.Priority;
        t.DueDateUtc = vm.DueDateUtc;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Task '{t.Title}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UserUpdate(DevUserUpdateVm vm)
    {
        var user = await _users.FindByIdAsync(vm.Id);
        if (user is null) return NotFound();
        user.FirstName = vm.FirstName.Trim();
        user.LastName = vm.LastName.Trim();
        user.Email = vm.Email.Trim();
        user.UserName = vm.Email.Trim();
        user.NormalizedEmail = _users.NormalizeEmail(vm.Email.Trim());
        user.NormalizedUserName = _users.NormalizeName(vm.Email.Trim());
        user.PhoneNumber = vm.PhoneNumber?.Trim();
        user.IsActive = vm.IsActive;
        await _users.UpdateAsync(user);

        if (!string.IsNullOrWhiteSpace(vm.NewPassword))
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            await _users.ResetPasswordAsync(user, token, vm.NewPassword);
        }

        TempData["Success"] = $"{user.FullName} updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CatalogueCreate(string name, CancellationToken ct)
    {
        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.AssetCatalogue.AnyAsync(a => a.Name.ToLower() == name.ToLower(), ct))
        {
            TempData["Error"] = $"'{name}' already exists in the catalogue.";
            return RedirectToAction(nameof(Index));
        }

        _db.AssetCatalogue.Add(new AssetCatalogue { Name = name });
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"'{name}' added to catalogue.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CatalogueDelete(int id, CancellationToken ct)
    {
        await _db.AssetCatalogue.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        TempData["Success"] = "Item deleted from catalogue.";
        return RedirectToAction(nameof(Index));
    }
}
