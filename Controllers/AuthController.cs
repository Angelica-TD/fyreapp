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
    [AllowAnonymous]
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

    // The page an emailed password link opens: choose a password, then sign in with it
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> SetPassword(string? userId, string? token)
    {
        var user = await LinkUserAsync(userId, token);
        return View(new SetPasswordVm { UserId = userId ?? "", Token = token ?? "", Email = user?.Email, Expired = user == null });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(SetPasswordVm vm)
    {
        var user = await LinkUserAsync(vm.UserId, vm.Token);
        if (user == null) return View(new SetPasswordVm { Expired = true });
        vm.Email = user.Email;
        if (!ModelState.IsValid) return View(vm);

        var result = await _userManager.ResetPasswordAsync(user, vm.Token, vm.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(vm);
        }

        // Following the link proves the email is theirs
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }
        await _userManager.ResetAccessFailedCountAsync(user);

        TempData["Success"] = "Your password is set. Sign in with your email and new password.";
        return Redirect("/");
    }

    // The login a password link is for, while the link is good: an active login, and a token not used or expired
    private async Task<ApplicationUser?> LinkUserAsync(string? userId, string? token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return null;
        var user = await _userManager.FindByIdAsync(userId);
        if (user is not { IsActive: true }) return null;
        var valid = await _userManager.VerifyUserTokenAsync(user, _userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token);
        return valid ? user : null;
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
