using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.UserManagement;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FyreApp.Tests.Services;

// People added in FyreApp on the Users page (e.g. a technician to test with): added, edited and deleted, unlike
// those from Uptick
public class PeopleServiceTests
{
    private static ServiceProvider Services()
    {
        var db = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(db));
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IPeopleService, PeopleService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task APerson_IsAddedAsFyreAppsOwn()
    {
        var sp = Services();
        var people = sp.GetRequiredService<IPeopleService>();

        var added = await people.CreateAsync(new PersonInput { Name = " Test Tech ", Email = "test.tech@fyrepower.com.au", LicenseType = "FIELD" });

        Assert.Equal(PersonStatus.Done, added.Status);
        var person = (await people.GetAsync(added.Id!.Value))!;
        Assert.Equal(("Test Tech", "test.tech@fyrepower.com.au", "FIELD", (long?)null), (person.Name, person.Email, person.LicenseType, person.UptickId));
    }

    [Fact]
    public async Task AnEmailOrLicenceTypeAlreadyTaken_IsRefused()
    {
        var sp = Services();
        var db = sp.GetRequiredService<AppDbContext>();
        db.UptickUsers.Add(new UptickUser { UptickId = 7, Name = "Dwayne Page", Email = "dwayne@fyrepower.com.au", LicenseType = "FIELD" });
        await db.SaveChangesAsync();
        var people = sp.GetRequiredService<IPeopleService>();

        var taken = await people.CreateAsync(new PersonInput { Name = "Test", Email = "Dwayne@FyrePower.com.au", LicenseType = "FIELD" });
        var customer = await people.CreateAsync(new PersonInput { Name = "Test", LicenseType = "CUSTOMER" });

        Assert.Equal(PersonStatus.EmailTaken, taken.Status);
        Assert.Contains("Dwayne Page", taken.Message);
        Assert.Equal(PersonStatus.Invalid, customer.Status);
    }

    [Fact]
    public async Task Editing_UpdatesTheirLogin_ButNotSomeoneFromUptick()
    {
        var sp = Services();
        var db = sp.GetRequiredService<AppDbContext>();
        var people = sp.GetRequiredService<IPeopleService>();
        var id = (await people.CreateAsync(new PersonInput { Name = "Test Tech", Email = "test@fyrepower.com.au", LicenseType = "FIELD" })).Id!.Value;
        await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(new ApplicationUser
        {
            UserName = "test@fyrepower.com.au", Email = "test@fyrepower.com.au", FirstName = "Test", LastName = "Tech", UptickUserId = id
        });
        var fromUptick = new UptickUser { UptickId = 7, Name = "Dwayne Page", LicenseType = "FIELD" };
        db.UptickUsers.Add(fromUptick);
        await db.SaveChangesAsync();

        var edited = await people.UpdateAsync(id, new PersonInput { Name = "Tess Tech", Email = "tess@fyrepower.com.au", LicenseType = "CONTRACTOR" });
        var refused = await people.UpdateAsync(fromUptick.Id, new PersonInput { Name = "Someone else", LicenseType = "FIELD" });

        Assert.Equal(PersonStatus.Done, edited.Status);
        var login = await db.Users.SingleAsync();
        Assert.Equal(("Tess", "Tech", "tess@fyrepower.com.au", "tess@fyrepower.com.au"), (login.FirstName, login.LastName, login.Email, login.UserName));
        Assert.Equal(PersonStatus.FromUptick, refused.Status);
        Assert.Equal("Dwayne Page", (await people.GetAsync(fromUptick.Id))!.Name);
    }

    [Fact]
    public async Task Deleting_TakesTheirLogin_AndUnassignsTheirTasks()
    {
        var sp = Services();
        var db = sp.GetRequiredService<AppDbContext>();
        var people = sp.GetRequiredService<IPeopleService>();
        var id = (await people.CreateAsync(new PersonInput { Name = "Test Tech", Email = "test@fyrepower.com.au", LicenseType = "FIELD" })).Id!.Value;
        await sp.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(new ApplicationUser
        {
            UserName = "test@fyrepower.com.au", Email = "test@fyrepower.com.au", UptickUserId = id
        });
        var site = new Property { Name = "Gemvale Glades", Client = new Client { Name = "Gemvale" } };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "Test task", AssignedToUptickUserId = id };
        db.ClientTasks.Add(task);
        var fromUptick = new UptickUser { UptickId = 7, Name = "Dwayne Page", LicenseType = "FIELD" };
        db.UptickUsers.Add(fromUptick);
        await db.SaveChangesAsync();

        var info = (await people.DeleteInfoAsync(id))!;
        var deleted = await people.DeleteAsync(id);
        var refused = await people.DeleteAsync(fromUptick.Id);

        Assert.Equal(("test@fyrepower.com.au", 1), (info.LoginEmail, info.TasksAssigned));
        Assert.Equal(PersonStatus.Done, deleted.Status);
        Assert.Null(await people.GetAsync(id));
        Assert.Empty(db.Users);
        Assert.Null((await db.ClientTasks.SingleAsync()).AssignedToUptickUserId);
        Assert.Equal(PersonStatus.FromUptick, refused.Status);
    }
}
