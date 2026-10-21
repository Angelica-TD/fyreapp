using System.Reflection;
using System.Security.Claims;
using FyreApp.Auth;
using FyreApp.Controllers;
using FyreApp.Hubs;
using FyreApp.Services.Lists;
using FyreApp.Services.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace FyreApp.Tests.Infrastructure;

// A Developer can do everything an Admin can; the Uptick migration and the catalogue are theirs alone
public class DeveloperRolesTests
{
    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)).Prepend(new Claim(ClaimTypes.Name, "someone")), "Test"));

    [Fact]
    public async Task ADeveloper_IsAnAdminToo()
    {
        var principal = await new DeveloperRoles().TransformAsync(Principal("Developer"));

        Assert.True(principal.IsInRole("Admin"));
        Assert.True(principal.IsInRole("Developer"));
    }

    [Fact]
    public async Task TheAdminRole_IsAddedOnce()
    {
        var roles = new DeveloperRoles();
        var principal = await roles.TransformAsync(await roles.TransformAsync(Principal("Developer")));

        Assert.Single(principal.FindAll(ClaimTypes.Role), c => c.Value == "Admin");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Tech")]
    public async Task OtherRoles_AreUnchanged(string role)
    {
        var principal = await new DeveloperRoles().TransformAsync(Principal(role));

        Assert.False(principal.IsInRole("Developer"));
        Assert.Equal(role == "Admin", principal.IsInRole("Admin"));
    }

    [Fact]
    public async Task ASignedOutVisitor_GetsNoRole()
    {
        var principal = await new DeveloperRoles().TransformAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Empty(principal.FindAll(ClaimTypes.Role));
    }

    [Theory]
    [InlineData(typeof(UptickMigrationController))]
    [InlineData(typeof(ImportProgressHub))]
    [InlineData(typeof(ServiceOfferingsController))]
    [InlineData(typeof(ProductsController))]
    [InlineData(typeof(AssetCatalogueController))]
    public void DeveloperOnlyPages_AreNotOpenToAdmins(Type type)
    {
        var roles = type.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles).ToList();

        Assert.Equal(new[] { "Developer" }, roles);
    }

    // The Users page: an Admin can't make anyone a Developer or change a Developer's login
    private static UsersController Users(string[] actorRoles, string targetRole = "Tech")
    {
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetAvailableRoles()).Returns(UserManagementService.Roles);
        service.Setup(s => s.GetByIdAsync("target")).ReturnsAsync(new UserViewModel { Id = "target", Role = targetRole });
        service.Setup(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new UserResetPasswordResult(UserResetPasswordStatus.Success));
        service.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(new UserDeleteResult(UserDeleteStatus.Success));

        var controller = new UsersController(service.Object, Mock.Of<IDirectoryListService>(), Mock.Of<ILoginEmailService>(),
            Mock.Of<FyreApp.Services.Email.IEmailService>(), Microsoft.Extensions.Options.Options.Create(new FyreApp.Services.Email.EmailOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(actorRoles) } },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(new DefaultHttpContext(), Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
        };
        return controller;
    }

    [Fact]
    public async Task AnAdmin_CantResetADevelopersPasswordOrDeleteThem()
    {
        var users = Users(["Admin"], targetRole: "Developer");

        Assert.IsType<ForbidResult>(await users.ResetPassword("target", new UserResetPasswordDto { NewPassword = "x", ConfirmPassword = "x" }));
        Assert.IsType<ForbidResult>(await users.Delete("target"));
        Assert.IsType<ForbidResult>(await users.Deactivate("target"));
        Assert.IsType<ForbidResult>(await users.Edit("target"));
    }

    [Fact]
    public async Task AnAdmin_CantMakeSomeoneADeveloper()
    {
        var users = Users(["Admin"]);

        Assert.IsType<ForbidResult>(await users.Edit("target", new UserEditDto { Role = "Developer" }));
        await users.Edit("target");
        Assert.DoesNotContain("Developer", (IEnumerable<string>)users.ViewBag.Roles);
    }

    [Fact]
    public async Task ADeveloper_ManagesDevelopers()
    {
        // As signed in: the Developer role plus the Admin role DeveloperRoles adds
        var users = Users(["Developer", "Admin"], targetRole: "Developer");

        Assert.IsType<RedirectToActionResult>(await users.Delete("target"));
        await users.Edit("target");
        Assert.Contains("Developer", (IEnumerable<string>)users.ViewBag.Roles);
    }
}
