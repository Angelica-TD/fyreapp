using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FyreApp.Infrastructure;

public sealed class BreadcrumbFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.Controller is not Controller controller) return;

        var cad = context.ActionDescriptor as ControllerActionDescriptor;
        if (cad == null) return;

        var controllerName = cad.ControllerName; // "Clients"
        var actionName = cad.ActionName;         // "Details"

        if (context.HttpContext.Request.Headers.Accept.ToString().Contains("application/json"))
            return;

        var crumbs = new List<(string Text, string? Url)>
        {
            ("Home", controller.Url.Action("Index", "Home"))
        };

        // Controller crumb
        crumbs.Add((SplitWords(controllerName), controller.Url.Action("Index", controllerName)));

        // Action crumb (skip Index so no "Clients > Index")
        if (!string.Equals(actionName, "Index", StringComparison.OrdinalIgnoreCase))
        {
            crumbs.Add((SplitWords(actionName), null));
        }

        controller.ViewData["Breadcrumbs"] = crumbs;
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static string SplitWords(string pascal)
    {
        // "MaintenanceSchedules" -> "Maintenance Schedules"
        if (string.IsNullOrWhiteSpace(pascal)) return pascal;

        var chars = pascal.ToCharArray();
        var result = new List<char>(chars.Length + 10);

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var prev = i > 0 ? chars[i - 1] : '\0';
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            var isBoundary =
                i > 0 &&
                char.IsUpper(c) &&
                (char.IsLower(prev) || (char.IsUpper(prev) && char.IsLower(next)));

            if (isBoundary) result.Add(' ');
            result.Add(c);
        }

        return new string(result.ToArray());
    }
}
