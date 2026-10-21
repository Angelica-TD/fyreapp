using System.Reflection;
using FyreApp.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FyreApp.Tests.Infrastructure;

// Every page needs a login (Program.cs sets a fallback policy); these are the only ones that don't.
// A new public page has to be added here on purpose.
public class AuthorizationTests
{
    private static readonly string[] PublicActions =
    {
        "Auth.Login",                 // the login form posts here
        "Auth.SetPassword",           // emailed password links (by single-use token)
        "Home.Index",                 // the login page
        "ServiceQuotes.Approve",      // client quote links (by unguessable token)
        "ServiceQuotes.ClientView",
        "ServiceQuotes.Decline"
    };

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null);

    private static string Name(MethodInfo m) => $"{m.DeclaringType!.Name.Replace("Controller", "")}.{m.Name}";

    private static readonly Type[] Controllers = typeof(HomeController).Assembly.GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
        .ToArray();

    [Fact]
    public void OnlyTheLoginPageAndQuoteLinks_AllowAnonymousUsers()
    {
        var anonymous = Controllers
            .SelectMany(c => c.GetCustomAttribute<AllowAnonymousAttribute>() != null
                ? Actions(c)
                : Actions(c).Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null))
            .Select(Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(PublicActions, anonymous);
    }

    // The sidebar shows these only to admins; the pages agree
    [Theory]
    [InlineData(typeof(RemarksController), "Index")]
    [InlineData(typeof(RemarksController), "Download")]
    [InlineData(typeof(ReportsController), "Index")]
    [InlineData(typeof(ReportsController), "Download")]
    [InlineData(typeof(PropertyController), "Index")]
    [InlineData(typeof(PropertyController), "Download")]
    [InlineData(typeof(AssetsController), "Index")]
    [InlineData(typeof(AssetsController), "Download")]
    [InlineData(typeof(AssetsController), "Suggestions")]
    public void ListPages_AreAdminOnly(Type controller, string action)
    {
        var roles = controller.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(controller.GetMethods().Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()))
            .Select(a => a.Roles);

        Assert.Contains("Admin", roles);
    }
}
