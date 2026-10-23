using FyreApp.Data;
using FyreApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Auth;

// A technician (a login without the Admin role) sees only the field app (My day), their own tasks, and the pages
// those lead to: their tasks' assets and remarks, and files. Everything else is closed to them, including pages added
// later, unless listed here. Admins (and Developers, who are Admins too) aren't affected.
public sealed class TechAccessFilter : IAsyncAuthorizationFilter
{
    // What a technician may open (controller, action); null: any action
    private static readonly (string Controller, string? Action, Check Check)[] Allowed =
    [
        ("Field", null, Check.None),
        ("Auth", null, Check.None),             // change password, log out
        ("Home", "Index", Check.None),          // sends them on to My day
        ("Home", "Error", Check.None),
        ("Task", "Index", Check.None),          // their own tasks only (TaskController)
        ("Task", "ApiSearch", Check.None),
        ("Task", "Download", Check.None),
        ("Task", "Details", Check.Task),
        ("Task", "Complete", Check.Task),
        ("Assets", "Details", Check.Asset),
        ("Remarks", "Details", Check.Remark),
        ("Files", "Get", Check.None)
    ];

    private enum Check { None, Task, Asset, Remark }

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public TechAccessFilter(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public static bool IsTech(System.Security.Claims.ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && !user.IsInRole(DeveloperRoles.Admin);

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (!IsTech(user)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        if (context.ActionDescriptor is not ControllerActionDescriptor action) return;

        var rule = Allowed.FirstOrDefault(a => a.Controller == action.ControllerName && (a.Action == null || a.Action == action.ActionName));
        if (rule.Controller == null)
        {
            context.Result = new ForbidResult();
            return;
        }
        if (rule.Check == Check.None) return;

        var me = (await _users.GetUserAsync(user))?.UptickUserId;
        var id = context.RouteData.Values.TryGetValue("id", out var raw) && int.TryParse(raw?.ToString(), out var n) ? n : (int?)null;
        if (me == null || id == null || !await AllowedAsync(rule.Check, me.Value, id.Value, context.HttpContext.RequestAborted))
            context.Result = new ForbidResult();
    }

    private Task<bool> AllowedAsync(Check check, int me, int id, CancellationToken ct) => check switch
    {
        Check.Task => Theirs(me).AnyAsync(t => t.Id == id, ct),
        Check.Asset => _db.Assets.AnyAsync(a => a.Id == id && Theirs(me).Any(t => t.PropertyId == a.PropertyId), ct),
        Check.Remark => _db.Remarks.AnyAsync(r => r.Id == id && Theirs(me).Any(t => t.PropertyId == r.PropertyId), ct),
        _ => Task.FromResult(true)
    };

    // The technician's tasks: assigned to them, or booked on one of their appointments (as the field app has them)
    private IQueryable<ClientTask> Theirs(int me) =>
        _db.ClientTasks.Where(t => t.AssignedToUptickUserId == me
                                   || _db.Appointments.Any(a => a.ClientTaskId == t.Id && a.Technicians.Any(u => u.Id == me)));
}
