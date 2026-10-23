using System.Security.Claims;
using FyreApp.Auth;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace FyreApp.Tests.Infrastructure;

// A technician reaches only My day, their own tasks and those tasks' assets and remarks
public class TechAccessTests
{
    private sealed record World(AppDbContext Db, UptickUser Me, ClientTask Mine, ClientTask Booked, ClientTask Others,
        Asset MyAsset, Asset OtherAsset, Remark MyRemark, Remark OtherRemark);

    private static async Task<World> SetupAsync()
    {
        var db = DbContextFactory.Create();
        var me = new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD" };
        var other = new UptickUser { Name = "Sam Tech", LicenseType = "FIELD" };
        var client = new Client { Name = "Gemvale" };
        var site = new Property { Name = "Gemvale Glades", Client = client };
        var elsewhere = new Property { Name = "Elsewhere", Client = client };
        var mine = new ClientTask { Property = site, Client = client, Title = "Mine", AssignedTo = me };
        var booked = new ClientTask { Property = site, Client = client, Title = "Booked", AssignedTo = other };
        var others = new ClientTask { Property = elsewhere, Client = client, Title = "Someone else's", AssignedTo = other };
        var myAsset = new Asset { Name = "FE 3", Property = site };
        var otherAsset = new Asset { Name = "FE 9", Property = elsewhere };
        var myRemark = new Remark { Property = site, Description = "Gauge in the red" };
        var otherRemark = new Remark { Property = elsewhere, Description = "Missing sign" };
        db.AddRange(mine, booked, others, myAsset, otherAsset, myRemark, otherRemark);
        db.Appointments.Add(new Appointment { ClientTask = booked, StartUtc = DateTime.UtcNow, Technicians = [me] });
        await db.SaveChangesAsync();
        return new World(db, me, mine, booked, others, myAsset, otherAsset, myRemark, otherRemark);
    }

    private static ClaimsPrincipal Signed(params string[] roles) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], "test"));

    private static async Task<IActionResult?> RunAsync(World w, ClaimsPrincipal user, string controller, string action, int? id = null, bool linked = true)
    {
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(u => u.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { UptickUserId = linked ? w.Me.Id : null });

        var route = new RouteData();
        if (id != null) route.Values["id"] = id.ToString();
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action, EndpointMetadata = [] };
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext { User = user }, route, descriptor), []);

        await new TechAccessFilter(w.Db, users.Object).OnAuthorizationAsync(context);
        return context.Result;
    }

    [Theory]
    [InlineData("Field", "Index")]
    [InlineData("Field", "Visit")]
    [InlineData("Task", "Index")]
    [InlineData("Task", "ApiSearch")]
    [InlineData("Auth", "ChangePassword")]
    [InlineData("Files", "Get")]
    public async Task ATech_CanOpenMyDayAndTheirTaskList(string controller, string action)
    {
        var w = await SetupAsync();
        Assert.Null(await RunAsync(w, Signed("Tech"), controller, action));
    }

    [Theory]
    [InlineData("Clients", "Index")]
    [InlineData("Property", "Details")]
    [InlineData("Quotes", "Index")]
    [InlineData("Task", "Create")]
    [InlineData("Task", "Edit")]
    [InlineData("Task", "Delete")]
    [InlineData("Reports", "Details")]
    [InlineData("Remarks", "Index")]
    [InlineData("NewPageNobodyListed", "Index")]
    public async Task ATech_IsKeptOutOfEverythingElse(string controller, string action)
    {
        var w = await SetupAsync();
        Assert.IsType<ForbidResult>(await RunAsync(w, Signed("Tech"), controller, action, 1));
    }

    [Fact]
    public async Task ATech_OpensOnlyTheirTasks_AndThoseTasksAssetsAndRemarks()
    {
        var w = await SetupAsync();
        var tech = Signed("Tech");

        Assert.Null(await RunAsync(w, tech, "Task", "Details", w.Mine.Id));
        Assert.Null(await RunAsync(w, tech, "Task", "Details", w.Booked.Id));
        Assert.IsType<ForbidResult>(await RunAsync(w, tech, "Task", "Details", w.Others.Id));
        Assert.IsType<ForbidResult>(await RunAsync(w, tech, "Task", "Complete", w.Others.Id));

        Assert.Null(await RunAsync(w, tech, "Assets", "Details", w.MyAsset.Id));
        Assert.IsType<ForbidResult>(await RunAsync(w, tech, "Assets", "Details", w.OtherAsset.Id));
        Assert.Null(await RunAsync(w, tech, "Remarks", "Details", w.MyRemark.Id));
        Assert.IsType<ForbidResult>(await RunAsync(w, tech, "Remarks", "Details", w.OtherRemark.Id));

        // A login not linked to a technician has no tasks
        Assert.IsType<ForbidResult>(await RunAsync(w, tech, "Task", "Details", w.Mine.Id, linked: false));
    }

    [Fact]
    public async Task AdminsAndDevelopers_AreNotAffected()
    {
        var w = await SetupAsync();
        Assert.Null(await RunAsync(w, Signed("Admin"), "Clients", "Index"));
        Assert.Null(await RunAsync(w, Signed("Developer", "Admin"), "Task", "Details", w.Others.Id));
    }

    [Fact]
    public void RemarksPages_StayAdminOnly_ExceptARemarksPage()
    {
        var roles = (string name) => typeof(FyreApp.Controllers.RemarksController).GetMethods()
            .Where(m => m.Name == name).SelectMany(m => m.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>())
            .Select(a => a.Roles).ToList();

        Assert.Contains("Admin", roles("Index"));
        Assert.Contains("Admin", roles("Download"));
        Assert.Contains("Admin", roles("BulkEdit"));
        Assert.DoesNotContain("Admin", roles("Details"));
    }
}
