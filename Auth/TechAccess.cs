using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FyreApp.Auth;

// A technician (a login without the Admin role) works in the field app alone: My day and their jobs (FieldController
// checks each job is theirs), the photos and signatures those show, and their password. Everything else is closed to
// them, including pages added later, unless listed here. Admins (and Developers, who are Admins too) aren't affected.
public sealed class TechAccessFilter : IAuthorizationFilter
{
    // What a technician may open (controller, action); null: any action
    private static readonly (string Controller, string? Action)[] Allowed =
    [
        ("Field", null),
        ("Auth", null),             // change password, log out
        ("Home", "Index"),          // sends them on to My day
        ("Home", "Error"),
        ("Files", "Get")
    ];

    public static bool IsTech(System.Security.Claims.ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && !user.IsInRole(DeveloperRoles.Admin);

    public static bool MayOpen(string controller, string action) =>
        Allowed.Any(a => a.Controller == controller && (a.Action == null || a.Action == action));

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!IsTech(context.HttpContext.User)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        if (context.ActionDescriptor is ControllerActionDescriptor action && !MayOpen(action.ControllerName, action.ActionName))
            context.Result = new ForbidResult();
    }
}
