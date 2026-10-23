using System.Security.Claims;
using FyreApp.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace FyreApp.Tests.Infrastructure;

// A technician works in the field app alone (My day, their jobs), and nowhere else in FyreApp
public class TechAccessTests
{
    private static ClaimsPrincipal Signed(params string[] roles) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], "test"));

    private static IActionResult? Run(ClaimsPrincipal user, string controller, string action)
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action, EndpointMetadata = [] };
        var context = new AuthorizationFilterContext(new ActionContext(new DefaultHttpContext { User = user }, new RouteData(), descriptor), []);
        new TechAccessFilter().OnAuthorization(context);
        return context.Result;
    }

    [Theory]
    [InlineData("Field", "Index")]
    [InlineData("Field", "Visit")]
    [InlineData("Field", "Remark")]
    [InlineData("Field", "Form")]
    [InlineData("Auth", "ChangePassword")]
    [InlineData("Auth", "Logout")]
    [InlineData("Home", "Index")]
    [InlineData("Files", "Get")]
    public void ATech_CanUseTheFieldApp(string controller, string action) =>
        Assert.Null(Run(Signed("Tech"), controller, action));

    [Theory]
    [InlineData("Task", "Index")]
    [InlineData("Task", "Details")]
    [InlineData("Task", "Edit")]
    [InlineData("Assets", "Details")]
    [InlineData("Remarks", "Details")]
    [InlineData("Clients", "Index")]
    [InlineData("Property", "Details")]
    [InlineData("Quotes", "Index")]
    [InlineData("Home", "MaintenanceEvents")]
    [InlineData("NewPageNobodyListed", "Index")]
    public void ATech_IsKeptOutOfEverythingElse(string controller, string action) =>
        Assert.IsType<ForbidResult>(Run(Signed("Tech"), controller, action));

    [Fact]
    public void AdminsAndDevelopers_AreNotAffected()
    {
        Assert.Null(Run(Signed("Admin"), "Clients", "Index"));
        Assert.Null(Run(Signed("Developer", "Admin"), "Task", "Details"));
    }
}
