using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Infrastructure;

public static class ListControllerExtensions
{
    // After a list page's bulk Edit: back to the same filtered list (only local URLs), with a message
    public static IActionResult BackToList(this Controller controller, string? returnUrl, string message)
    {
        controller.TempData["Success"] = message;
        return controller.Url.IsLocalUrl(returnUrl)
            ? controller.LocalRedirect(returnUrl!)
            : controller.RedirectToAction("Index");
    }

    public static string Plural(int n, string singular, string plural) => $"{n:N0} {(n == 1 ? singular : plural)}";
}
