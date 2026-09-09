using FyreApp.Services.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly IUserManagementService _userService;

    public UsersController(IUserManagementService userService)
    {
        _userService = userService;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userService.GetAllAsync();
        return View(users);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.Roles = _userService.GetAvailableRoles();
        return View(new UserCreateDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateDto dto)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Roles = _userService.GetAvailableRoles();
            return View(dto);
        }

        var result = await _userService.CreateAsync(dto);

        switch (result.Status)
        {
            case UserCreateStatus.Success:
                TempData["Success"] = "User created successfully.";
                return RedirectToAction(nameof(Index));

            case UserCreateStatus.EmailAlreadyExists:
                ModelState.AddModelError(nameof(dto.Email), "A user with this email already exists.");
                ViewBag.Roles = _userService.GetAvailableRoles();
                return View(dto);

            case UserCreateStatus.InvalidRole:
                ModelState.AddModelError(nameof(dto.Role), "Invalid role selected.");
                ViewBag.Roles = _userService.GetAvailableRoles();
                return View(dto);

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                ViewBag.Roles = _userService.GetAvailableRoles();
                return View(dto);
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

        ViewBag.UserId = id;
        ViewBag.Roles = _userService.GetAvailableRoles();
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserEditDto dto)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.UserId = id;
            ViewBag.Roles = _userService.GetAvailableRoles();
            return View(dto);
        }

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
                ViewBag.UserId = id;
                ViewBag.Roles = _userService.GetAvailableRoles();
                return View(dto);

            case UserUpdateStatus.InvalidRole:
                ModelState.AddModelError(nameof(dto.Role), "Invalid role selected.");
                ViewBag.UserId = id;
                ViewBag.Roles = _userService.GetAvailableRoles();
                return View(dto);

            default:
                foreach (var error in result.Errors ?? [])
                    ModelState.AddModelError(string.Empty, error);
                ViewBag.UserId = id;
                ViewBag.Roles = _userService.GetAvailableRoles();
                return View(dto);
        }
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
}
