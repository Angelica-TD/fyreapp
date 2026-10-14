using FyreApp.Infrastructure;
using FyreApp.Services.Lists;
using FyreApp.Services.UserManagement;
using FyreApp.ViewModels.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly IUserManagementService _userService;
    private readonly IDirectoryListService _directory;

    public UsersController(IUserManagementService userService, IDirectoryListService directory)
    {
        _userService = userService;
        _directory = directory;
    }

    // Everyone in Uptick (the source of truth for who's who) with their FyreApp login, plus any logins
    // not linked to an Uptick user yet
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? license, string? login, int page = 1, CancellationToken ct = default) =>
        View(new UsersIndexVm
        {
            List = await _directory.UptickUsersAsync(UsersFilter(search, license, login, page), ct),
            Unlinked = await _userService.GetUnlinkedAsync(),
            Linkable = await _userService.GetLinkableUptickUsersAsync()
        });

    // The old "Uptick users" page, now the Users page
    [HttpGet]
    public IActionResult Uptick(string? search, string? license, int page = 1) =>
        RedirectToAction(nameof(Index), new { search, license, page });

    [HttpGet]
    public async Task<IActionResult> Download(string? search, string? license, string? login, CancellationToken ct = default) =>
        File(await _directory.UptickUsersCsvAsync(UsersFilter(search, license, login, 1), ct), "text/csv", CsvExport.FileName("users"));

    private static DirectoryFilter UsersFilter(string? search, string? license, string? login, int page) => new()
    {
        Search = search, License = license, Active = "any", Page = page,
        Login = login?.ToLowerInvariant() is "yes" or "no" ? login.ToLowerInvariant() : null
    };

    // Give an Uptick user a FyreApp login
    [HttpGet]
    public async Task<IActionResult> Create(int uptickUserId)
    {
        var uptick = await _userService.GetUptickUserAsync(uptickUserId);
        if (uptick == null) return NotFound();

        ViewBag.Uptick = uptick;
        ViewBag.Roles = _userService.GetAvailableRoles();
        return View(new UserCreateDto
        {
            UptickUserId = uptickUserId,
            Role = uptick.LicenseType == "DESK" ? "Admin" : "Tech"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateDto dto)
    {
        var uptick = await _userService.GetUptickUserAsync(dto.UptickUserId);
        if (uptick == null) return NotFound();

        IActionResult Form()
        {
            ViewBag.Uptick = uptick;
            ViewBag.Roles = _userService.GetAvailableRoles();
            return View(dto);
        }

        if (!ModelState.IsValid) return Form();

        var result = await _userService.CreateAsync(dto);

        switch (result.Status)
        {
            case UserCreateStatus.Success:
                TempData["Success"] = $"{uptick.Name} can now sign in to FyreApp.";
                return RedirectToAction(nameof(Index));

            case UserCreateStatus.UptickUserNotFound:
                return NotFound();

            case UserCreateStatus.NoEmail:
                ModelState.AddModelError(string.Empty, "This user has no email in Uptick. Add one in Uptick and import the Users export again.");
                return Form();

            case UserCreateStatus.AlreadyHasLogin:
                TempData["Success"] = $"{uptick.Name} already has a FyreApp login.";
                return RedirectToAction(nameof(Index));

            case UserCreateStatus.EmailAlreadyExists:
                ModelState.AddModelError(string.Empty,
                    "A FyreApp login already uses this email. Link that login to this Uptick user from the Users page instead.");
                return Form();

            case UserCreateStatus.InvalidRole:
                ModelState.AddModelError(nameof(dto.Role), "Invalid role selected.");
                return Form();

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                return Form();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user == null) return NotFound();

        var dto = new UserEditDto
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role
        };

        ViewBag.User = user;
        ViewBag.Roles = _userService.GetAvailableRoles();
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserEditDto dto)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user == null) return NotFound();

        IActionResult Form()
        {
            ViewBag.User = user;
            ViewBag.Roles = _userService.GetAvailableRoles();
            return View(dto);
        }

        // A linked login's name and email come from Uptick, so the form doesn't send them
        if (user.UptickUserId != null)
            foreach (var key in new[] { nameof(dto.FirstName), nameof(dto.LastName), nameof(dto.Email) })
                ModelState.Remove(key);

        if (!ModelState.IsValid) return Form();

        var result = await _userService.UpdateAsync(id, dto);

        switch (result.Status)
        {
            case UserUpdateStatus.Success:
                TempData["Success"] = "User updated successfully.";
                return RedirectToAction(nameof(Index));

            case UserUpdateStatus.NotFound:
                return NotFound();

            case UserUpdateStatus.EmailAlreadyExists:
                ModelState.AddModelError(nameof(dto.Email), "A user with this email already exists.");
                return Form();

            case UserUpdateStatus.InvalidRole:
                ModelState.AddModelError(nameof(dto.Role), "Invalid role selected.");
                return Form();

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                return Form();
        }
    }

    // Link a login made before Uptick was imported to its Uptick user
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(string id, int uptickUserId)
    {
        var result = await _userService.LinkAsync(id, uptickUserId);
        switch (result.Status)
        {
            case UserLinkStatus.Success:
                TempData["Success"] = result.Problem ?? "Login linked; it now takes its name and email from Uptick.";
                return RedirectToAction(nameof(Index));
            case UserLinkStatus.AlreadyHasLogin:
                TempData["Error"] = "That Uptick user already has a FyreApp login.";
                return RedirectToAction(nameof(Index));
            default:
                return NotFound();
        }
    }

    // Link logins to Uptick users by email, refresh every linked login's name and email from Uptick, and
    // assign imported tasks to the Uptick user their "Assigned Technician" names
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync()
    {
        var result = await _userService.SyncFromUptickAsync();
        var parts = new List<string>
        {
            $"Logins: {result.Linked} linked by email, {result.Updated} updated from Uptick. " +
            $"Imported tasks: {result.TasksAssigned} assigned to the technician Uptick names."
        };
        parts.AddRange(result.Problems);
        TempData["Success"] = string.Join(" ", parts);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id)
    {
        await _userService.DeactivateAsync(id);
        TempData["Success"] = "User deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(string id)
    {
        await _userService.ActivateAsync(id);
        TempData["Success"] = "User activated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _userService.DeleteAsync(id);
        TempData["Success"] = "User deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user == null) return NotFound();

        ViewBag.UserId = id;
        ViewBag.UserName = user.FullName;
        return View(new UserResetPasswordDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, UserResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
        {
            var user = await _userService.GetByIdAsync(id);
            ViewBag.UserId = id;
            ViewBag.UserName = user?.FullName ?? string.Empty;
            return View(dto);
        }

        var result = await _userService.ResetPasswordAsync(id, dto.NewPassword);

        switch (result.Status)
        {
            case UserResetPasswordStatus.Success:
                TempData["Success"] = "Password updated.";
                return RedirectToAction(nameof(Index));

            case UserResetPasswordStatus.NotFound:
                return NotFound();

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                var u = await _userService.GetByIdAsync(id);
                ViewBag.UserId = id;
                ViewBag.UserName = u?.FullName ?? string.Empty;
                return View(dto);
        }
    }
}
