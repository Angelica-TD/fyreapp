using System.Net;
using System.Text.RegularExpressions;
using FyreApp.Controllers;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Email;
using FyreApp.Services.Lists;
using FyreApp.Services.UserManagement;
using FyreApp.ViewModels.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// A new login's welcome email (or a link sent from the Users page) and the page it opens to choose a password
public class PasswordLinkTests
{
    private const string SetPasswordUrl = "https://app.fyrepower.com.au/Auth/SetPassword";

    private sealed class Sent
    {
        public string? To, Subject, Body;
    }

    private static (ServiceProvider Services, Sent Sent) Setup(bool emailConfigured = true, Exception? smtpError = null)
    {
        var sent = new Sent();
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(emailConfigured);
        var send = email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (smtpError != null) send.ThrowsAsync(smtpError);
        else send.Callback<string, string, string, string, CancellationToken>((to, _, subject, body, _) => (sent.To, sent.Subject, sent.Body) = (to, subject, body))
            .Returns(Task.CompletedTask);

        var db = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(db));
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton(email.Object);
        services.AddScoped<ILoginEmailService, LoginEmailService>();
        return (services.BuildServiceProvider(), sent);
    }

    private static async Task<ApplicationUser> NewLoginAsync(IServiceProvider sp, bool active = true)
    {
        var user = new ApplicationUser
        {
            FirstName = "Sam", LastName = "Tech", Email = "sam@fyrepower.com.au", UserName = "sam@fyrepower.com.au", IsActive = active
        };
        var result = await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);   // no password yet
        Assert.True(result.Succeeded);
        return user;
    }

    // The userId and token from the emailed link
    private static (string UserId, string Token) Link(string body)
    {
        var href = WebUtility.HtmlDecode(Regex.Match(body, "href=\"([^\"]+)\"").Groups[1].Value);
        Assert.StartsWith(SetPasswordUrl + "?", href);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(href).Query);
        return (query["userId"]!, query["token"]!);
    }

    private static AuthController Auth(IServiceProvider sp) =>
        new(null!, sp.GetRequiredService<UserManager<ApplicationUser>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

    [Fact]
    public async Task TheWelcomeEmail_LinksToChoosingAPassword_WhichThenSignsIn()
    {
        var (sp, sent) = Setup();
        var user = await NewLoginAsync(sp);

        var result = await sp.GetRequiredService<ILoginEmailService>().SendPasswordLinkAsync(user.Id, SetPasswordUrl, welcome: true);

        Assert.Equal(PasswordLinkStatus.Sent, result.Status);
        Assert.Equal("sam@fyrepower.com.au", sent.To);
        Assert.Equal("Your FyreApp login", sent.Subject);
        Assert.Contains("Hi Sam,", sent.Body);

        var (userId, token) = Link(sent.Body!);
        var auth = Auth(sp);
        var page = Assert.IsType<ViewResult>(await auth.SetPassword(userId, token));
        Assert.False(((SetPasswordVm)page.Model!).Expired);

        var done = await auth.SetPassword(new SetPasswordVm { UserId = userId, Token = token, NewPassword = "Fyre-pass1", ConfirmPassword = "Fyre-pass1" });

        Assert.IsType<RedirectResult>(done);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var saved = (await users.FindByIdAsync(user.Id))!;
        Assert.True(await users.CheckPasswordAsync(saved, "Fyre-pass1"));
        Assert.True(saved.EmailConfirmed);
    }

    [Fact]
    public async Task ALink_WorksOnce()
    {
        var (sp, sent) = Setup();
        var user = await NewLoginAsync(sp);
        await sp.GetRequiredService<ILoginEmailService>().SendPasswordLinkAsync(user.Id, SetPasswordUrl, welcome: false);
        var (userId, token) = Link(sent.Body!);
        var auth = Auth(sp);

        await auth.SetPassword(new SetPasswordVm { UserId = userId, Token = token, NewPassword = "Fyre-pass1", ConfirmPassword = "Fyre-pass1" });
        var again = Assert.IsType<ViewResult>(await auth.SetPassword(userId, token));

        Assert.True(((SetPasswordVm)again.Model!).Expired);
        Assert.Equal("Set your FyreApp password", sent.Subject);
    }

    [Fact]
    public async Task AMadeUpLink_IsExpired()
    {
        var (sp, _) = Setup();
        var user = await NewLoginAsync(sp);

        var page = Assert.IsType<ViewResult>(await Auth(sp).SetPassword(user.Id, "not-a-token"));

        Assert.True(((SetPasswordVm)page.Model!).Expired);
    }

    [Fact]
    public async Task ADeactivatedLogin_GetsNoLink_AndItsOldLinkStopsWorking()
    {
        var (sp, sent) = Setup();
        var user = await NewLoginAsync(sp);
        await sp.GetRequiredService<ILoginEmailService>().SendPasswordLinkAsync(user.Id, SetPasswordUrl, welcome: true);
        var (userId, token) = Link(sent.Body!);

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        user.IsActive = false;
        await users.UpdateAsync(user);
        sent.To = null;

        var result = await sp.GetRequiredService<ILoginEmailService>().SendPasswordLinkAsync(user.Id, SetPasswordUrl, welcome: false);
        var page = Assert.IsType<ViewResult>(await Auth(sp).SetPassword(userId, token));

        Assert.Equal(PasswordLinkStatus.Inactive, result.Status);
        Assert.Null(sent.To);
        Assert.True(((SetPasswordVm)page.Model!).Expired);
    }

    [Fact]
    public async Task WithoutEmailSetUp_NothingIsSent()
    {
        var (sp, sent) = Setup(emailConfigured: false);
        var user = await NewLoginAsync(sp);

        var result = await sp.GetRequiredService<ILoginEmailService>().SendPasswordLinkAsync(user.Id, SetPasswordUrl, welcome: true);

        Assert.Equal(PasswordLinkStatus.EmailNotSetUp, result.Status);
        Assert.Null(sent.To);
    }

    [Fact]
    public async Task AnEmailServerError_IsReported()
    {
        var (sp, _) = Setup(smtpError: new InvalidOperationException("535 Authentication Credentials Invalid"));
        var user = await NewLoginAsync(sp);

        var result = await sp.GetRequiredService<ILoginEmailService>().SendPasswordLinkAsync(user.Id, SetPasswordUrl, welcome: true);

        Assert.Equal(PasswordLinkStatus.Failed, result.Status);
        Assert.Equal("535 Authentication Credentials Invalid", result.Error);
    }

    // Users → give a login
    private static (UsersController Controller, Mock<IUserManagementService> Users, Mock<ILoginEmailService> Links) Users(bool emailConfigured)
    {
        var users = new Mock<IUserManagementService>();
        users.Setup(s => s.GetAvailableRoles()).Returns(UserManagementService.Roles);
        users.Setup(s => s.GetUptickUserAsync(7)).ReturnsAsync(new UptickUser { Id = 7, Name = "Sam Tech", Email = "sam@fyrepower.com.au" });
        users.Setup(s => s.CreateAsync(It.IsAny<UserCreateDto>())).ReturnsAsync(new UserCreateResult(UserCreateStatus.Success, UserId: "new"));

        var links = new Mock<ILoginEmailService>();
        links.Setup(l => l.SendPasswordLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordLinkResult(PasswordLinkStatus.Sent));

        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(emailConfigured);

        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/Auth/SetPassword");

        var controller = new UsersController(users.Object, Mock.Of<IDirectoryListService>(), links.Object, email.Object,
            Options.Create(new EmailOptions { SiteUrl = "https://app.fyrepower.com.au/" }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>()),
            Url = url.Object
        };
        return (controller, users, links);
    }

    [Fact]
    public async Task GivingALogin_EmailsTheWelcomeLink_WithNoPasswordSet()
    {
        var (controller, users, links) = Users(emailConfigured: true);

        var result = await controller.Create(new UserCreateDto { UptickUserId = 7, Role = "Tech", EmailPasswordLink = true, Password = "ignored" });

        Assert.IsType<RedirectToActionResult>(result);
        users.Verify(u => u.CreateAsync(It.Is<UserCreateDto>(d => d.Password == null)));
        links.Verify(l => l.SendPasswordLinkAsync("new", SetPasswordUrl, true, It.IsAny<CancellationToken>()));
        Assert.Contains("welcome email", (string)controller.TempData["Success"]!);
    }

    [Fact]
    public async Task WithoutEmailSetUp_GivingALogin_NeedsAPassword()
    {
        var (controller, users, _) = Users(emailConfigured: false);

        var emailed = await controller.Create(new UserCreateDto { UptickUserId = 7, Role = "Tech", EmailPasswordLink = true });
        var noPassword = await controller.Create(new UserCreateDto { UptickUserId = 7, Role = "Tech", EmailPasswordLink = false });

        Assert.IsType<ViewResult>(emailed);
        Assert.IsType<ViewResult>(noPassword);
        users.Verify(u => u.CreateAsync(It.IsAny<UserCreateDto>()), Times.Never);
    }
}
