using FyreApp.Models;
using FyreApp.ViewModels.Home;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(HomeIndexVm vm)
    {
        vm.ShowLogin = true;

        // ---- Sanitisation (server-side) ----
        vm.Login.Email = (vm.Login.Email ?? string.Empty).Trim();
        vm.Login.ReturnUrl = string.IsNullOrWhiteSpace(vm.Login.ReturnUrl)
            ? Url.Action("Index", "Home")
            : vm.Login.ReturnUrl;

        // Only allow local redirects (prevents open-redirect attacks)
        var returnUrl = Url.IsLocalUrl(vm.Login.ReturnUrl) ? vm.Login.ReturnUrl! : Url.Action("Index", "Home")!;

        // ---- Validation (server-side) ----
        if (!ModelState.IsValid)
            return View("~/Views/Home/Index.cshtml", vm);

        // Normalise email to match Identity behavior
        var user = await _userManager.FindByEmailAsync(vm.Login.Email);
        if (user == null)
        {
            // Don’t reveal whether the account exists (prevents account enumeration)
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View("~/Views/Home/Index.cshtml", vm);
        }

        // Identity handles password verification + lockout policies
        var result = await _signInManager.PasswordSignInAsync(
            userName: user.UserName!,
            password: vm.Login.Password,
            isPersistent: vm.Login.RememberMe,
            lockoutOnFailure: true
        );

        if (result.Succeeded)
            return LocalRedirect(returnUrl);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked. Please try again later.");
            return View("~/Views/Home/Index.cshtml", vm);
        }

        if (result.RequiresTwoFactor)
        {
            // 2FA later
            ModelState.AddModelError(string.Empty, "Two-factor authentication is required for this account.");
            return View("~/Views/Home/Index.cshtml", vm);
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View("~/Views/Home/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
