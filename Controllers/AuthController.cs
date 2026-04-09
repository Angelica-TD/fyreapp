using FyreApp.Models;
using FyreApp.ViewModels.Auth;
using FyreApp.ViewModels.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(HomeIndexVm vm)
    {
        vm ??= new HomeIndexVm();
        vm.Login ??= new();
        vm.ShowLogin = true;

        // Sanitise
        vm.Login.Email = (vm.Login.Email ?? string.Empty).Trim();
        vm.Login.ReturnUrl = string.IsNullOrWhiteSpace(vm.Login.ReturnUrl)
            ? Url.Action("Index", "Home")
            : vm.Login.ReturnUrl;

        // Validate return url (open redirect protection)
        var returnUrl = Url.IsLocalUrl(vm.Login.ReturnUrl)
            ? vm.Login.ReturnUrl!
            : Url.Action("Index", "Home")!;

        if (!ModelState.IsValid)
        {
            vm.Login.Password = string.Empty;
            return View("~/Views/Home/Index.cshtml", vm);
        }

        var user = await _userManager.FindByEmailAsync(vm.Login.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            vm.Login.Password = string.Empty;
            return View("~/Views/Home/Index.cshtml", vm);
        }

        var result = await _signInManager.PasswordSignInAsync(
            userName: user.UserName!,
            password: vm.Login.Password,
            isPersistent: vm.Login.RememberMe,
            lockoutOnFailure: true
        );

        if (result.Succeeded)
            return LocalRedirect(returnUrl);

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "This account is locked. Please try again later.");
        else if (result.RequiresTwoFactor)
            ModelState.AddModelError(string.Empty, "Two-factor authentication is required for this account.");
        else
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");

        vm.Login.Password = string.Empty;
        return View("~/Views/Home/Index.cshtml", vm);
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _userManager.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(vm);
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction(nameof(ChangePassword));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }
}
