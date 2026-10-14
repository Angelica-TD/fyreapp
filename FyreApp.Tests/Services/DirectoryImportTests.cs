using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Services.Lists;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Uptick's Users, Contractors and Suppliers exports, and their list pages
public class DirectoryImportTests
{
    // Header lines as Uptick writes them (users has two "License" columns)
    private const string UserHeader = "\"ID\",\"Date joined\",\"Last login\",\"Name\",\"Email\",\"License\",\"Partner UID\",\"Security Groups\",\"Home Branch\",\"Allowed Branches\",\"Service Group\",\"Cost Rate\",\"Sell Rate\",\"License\"";
    private const string ContractorHeader = "\"ID\",\"Name\",\"Created\",\"Updated\",\"Primary Contact\",\"Email\",\"Email CC\",\"Phone BH\",\"Mobile\",\"Starred\",\"Active\",\"Partner UID\",\"Category\",\"Tags\",\"Address\",\"Skills\",\"Service Areas\"";
    private const string SupplierHeader = "\"ID\",\"Created\",\"Updated\",\"Is Active\",\"Name\",\"Ref\",\"Business Number\",\"Partner UID\",\"Partner Terms\",\"Payment Day\",\"Accounts Contact Name\",\"Accounts Email\",\"Purchasing Contact Name\",\"Purchasing Email\",\"Accounting Organisation\"";

    private static Stream Csv(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    // Export type left to detection, as when an admin just drops the file in
    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, Stream csv) =>
        new UptickImportService(new UptickImporter[]
        {
            new PropertyImporter(db), new ClientContactImporter(db), new TaskImporter(db),
            new UserImporter(db), new ContractorImporter(db), new SupplierImporter(db)
        }).ImportAsync(csv, "export.csv", null, dryRun: false);

    [Fact]
    public async Task Users_KeepBothLicenseColumns_AndUpdateOnRerun()
    {
        using var db = DbContextFactory.Create();

        var first = await Import(db, Csv(UserHeader,
            "9,\"2021-02-18 21:27:16\",\"2026-10-13 15:16:23\",\"Dwayne Page\",\"fyretech02@gmail.com\",\"FIELD\",\"\",\"Technician - Technician - Higher Permissions\",\"\",\"\",\"Servicing - Portables & Fire Equipment\",\"\",\"\",\"QBCC 15366855\"",
            "13,\"2021-06-22 14:10:43\",\"\",\"Mermaid Central\",\"graham@auadmin.com.au\",\"CUSTOMER\",\"\",\"Client\",\"\",\"\",\"\",\"\",\"\",\"\""));
        Assert.Equal((UptickExportType?)UptickExportType.Users, first.Type);
        Assert.Equal(2, first.Created);

        var dwayne = await db.UptickUsers.SingleAsync(u => u.ExternalId == "9");
        Assert.Equal(("FIELD", "QBCC 15366855"), (dwayne.LicenseType, dwayne.Licence));
        Assert.Equal("Servicing - Portables & Fire Equipment", dwayne.ServiceGroup);
        // Uptick times are Brisbane time (UTC+10)
        Assert.Equal(new DateTime(2026, 10, 13, 5, 16, 23, DateTimeKind.Utc), dwayne.LastLoginUtc);
        Assert.Null((await db.UptickUsers.SingleAsync(u => u.ExternalId == "13")).LastLoginUtc);

        var second = await Import(db, Csv(UserHeader,
            "9,\"2021-02-18 21:27:16\",\"2026-10-14 07:00:00\",\"Dwayne Page (Field Tech)\",\"fyretech02@gmail.com\",\"FIELD\",\"\",\"Technician\",\"\",\"\",\"\",\"\",\"\",\"QBCC 15366855\""));
        Assert.Equal((0, 1), (second.Created, second.Updated));
        Assert.Equal(2, await db.UptickUsers.CountAsync());
        Assert.Equal("Dwayne Page (Field Tech)", (await db.UptickUsers.SingleAsync(u => u.ExternalId == "9")).Name);
    }

    [Fact]
    public async Task Contractors_Import_AndListActiveByDefault()
    {
        using var db = DbContextFactory.Create();

        var result = await Import(db, Csv(ContractorHeader,
            "5,\"CR&K Kay Plumbing\",\"2023-06-07 11:29:40\",\"2025-06-12 14:42:31\",\"\",\"colkayplumbing@outlook.com\",\"admin@fyrepower.com.au\nstephenlambert64@hotmail.com\",\"\",\"\",False,True,\"\",\"\",\"\",\"\",\"\",\"\"",
            "4,\"Rays Advantage Electrical\",\"2022-01-13 12:53:51\",\"2025-09-09 14:42:43\",\"\",\"ray@example.com\",\"\",\"0488 883 973\",\"\",True,True,\"\",\"General\",\"\",\"\",\"\",\"\"",
            "6,\"Old Sparky\",\"2022-01-13 12:53:51\",\"2025-09-09 14:42:43\",\"\",\"\",\"\",\"\",\"\",False,False,\"\",\"\",\"\",\"\",\"\",\"\""));

        Assert.Equal((UptickExportType?)UptickExportType.Contractors, result.Type);
        var kay = await db.Contractors.SingleAsync(c => c.ExternalId == "5");
        Assert.Equal("admin@fyrepower.com.au\nstephenlambert64@hotmail.com", kay.EmailCc);

        var list = new DirectoryListService(db);
        var active = await list.ContractorsAsync(new DirectoryFilter());
        // Starred first, then by name; the inactive one is hidden
        Assert.Equal(new[] { "Rays Advantage Electrical", "CR&K Kay Plumbing" }, active.Items.Select(c => c.Name));
        var any = await list.ContractorsAsync(new DirectoryFilter { Active = "any", Search = "sparky" });
        Assert.Equal("Old Sparky", Assert.Single(any.Items).Name);
    }

    [Fact]
    public async Task Suppliers_Import_AndSearchByAbnIgnoringSpaces()
    {
        using var db = DbContextFactory.Create();

        var result = await Import(db, Csv(SupplierHeader,
            "175,\"2021-07-16 08:50:07\",\"2026-07-20 10:06:05\",True,\"Auscoast Fire Services\",\"\",\"91 123 601 276\",\"8a81\",\"\",\"\",\"Jade\",\"ap@auscoastfire.com.au\",\"Auscoast Fire Services\",\"jade@auscoastfire.com.au\",\"Fyrepower Fire Protection\"",
            "5,\"2021-03-02 16:47:35\",\"2026-03-23 13:18:58\",False,\"CALTEX CLEARING\",\"\",\"\",\"1a59\",\"\",\"\",\"CALTEX CLEARING\",\"\",\"CALTEX CLEARING\",\"\",\"Fyrepower Fire Protection\""));

        Assert.Equal((UptickExportType?)UptickExportType.Suppliers, result.Type);
        Assert.False((await db.Suppliers.SingleAsync(s => s.ExternalId == "5")).Active);

        var found = await new DirectoryListService(db).SuppliersAsync(new DirectoryFilter { Search = "91123601276" });
        var auscoast = Assert.Single(found.Items);
        Assert.Equal(("Jade", "ap@auscoastfire.com.au"), (auscoast.AccountsContactName, auscoast.AccountsEmail));
    }

    [Theory]
    [InlineData("DAYSAFTERDATE", "14", "14 days after invoice date")]
    [InlineData("DAYOFFOLLOWINGMONTH", "30", "Day 30 of the following month")]
    [InlineData(null, null, null)]
    public void Supplier_PaymentTermsInWords(string? terms, string? day, string? expected) =>
        Assert.Equal(expected, new Supplier { PaymentTerms = terms, PaymentDay = day }.PaymentTermsLabel);

    [Fact]
    public async Task UptickUsers_StaffFirst_FilterByLicenceAndLogin_AndShowLinkedLogins()
    {
        using var db = DbContextFactory.Create();
        var dwayne = new UptickUser { ExternalId = "9", Name = "Dwayne Page", Email = "FyreTech02@gmail.com", LicenseType = "FIELD" };
        db.UptickUsers.AddRange(
            new UptickUser { ExternalId = "13", Name = "Mermaid Central", Email = "graham@auadmin.com.au", LicenseType = "CUSTOMER" },
            dwayne,
            new UptickUser { ExternalId = "3", Name = "FyrePower Team", Email = "admin@fyrepower.com.au", LicenseType = "DESK" });
        await db.SaveChangesAsync();
        db.Users.Add(new ApplicationUser { Id = "dwayne", Email = "fyretech02@gmail.com", UserName = "fyretech02@gmail.com", UptickUserId = dwayne.Id });
        await db.SaveChangesAsync();
        var list = new DirectoryListService(db);

        var all = await list.UptickUsersAsync(new DirectoryFilter { Active = "any" });
        Assert.Equal(new[] { "FyrePower Team", "Dwayne Page", "Mermaid Central" }, all.Items.Select(i => i.User.Name));
        Assert.Equal(new[] { null, "dwayne", null }, all.Items.Select(i => i.Login?.Id));

        var customers = await list.UptickUsersAsync(new DirectoryFilter { Active = "any", License = "customer" });
        Assert.Equal("Mermaid Central", Assert.Single(customers.Items).User.Name);

        var withLogin = await list.UptickUsersAsync(new DirectoryFilter { Active = "any", Login = "yes" });
        Assert.Equal("Dwayne Page", Assert.Single(withLogin.Items).User.Name);
        var withoutLogin = await list.UptickUsersAsync(new DirectoryFilter { Active = "any", Login = "no" });
        Assert.Equal(2, withoutLogin.Total);
    }

    [Fact]
    public async Task UsersImport_LinksLoginsByEmail_AndTheyTakeTheirNameAndEmailFromUptick()
    {
        using var db = DbContextFactory.Create();
        // Made in FyreApp before Uptick was imported
        db.Users.AddRange(
            new ApplicationUser { Id = "dwayne", FirstName = "D", LastName = "P", Email = "FYRETECH02@gmail.com", UserName = "FYRETECH02@gmail.com" },
            new ApplicationUser { Id = "admin", FirstName = "Site", LastName = "Admin", Email = "admin@example.com", UserName = "admin@example.com" });
        await db.SaveChangesAsync();

        var result = await Import(db, Csv(UserHeader,
            "9,\"2021-02-18 21:27:16\",\"\",\"Dwayne Page (Field Tech)\",\"fyretech02@gmail.com\",\"FIELD\",\"\",\"Technician\",\"\",\"\",\"\",\"\",\"\",\"\""));

        Assert.Contains(result.Notes, n => n.Contains("1 FyreApp login was linked"));
        var login = await db.Users.SingleAsync(u => u.Id == "dwayne");
        var uptick = await db.UptickUsers.SingleAsync(u => u.ExternalId == "9");
        Assert.Equal(uptick.Id, login.UptickUserId);
        Assert.Equal(("Dwayne", "Page"), (login.FirstName, login.LastName));
        Assert.Equal(("fyretech02@gmail.com", "FYRETECH02@GMAIL.COM"), (login.Email, login.NormalizedEmail));
        // Not in Uptick: left as it is
        var admin = await db.Users.SingleAsync(u => u.Id == "admin");
        Assert.Equal((null, "Site"), (admin.UptickUserId, admin.FirstName));

        // Renamed and new email in Uptick: the linked login follows
        await Import(db, Csv(UserHeader,
            "9,\"2021-02-18 21:27:16\",\"\",\"Dwayne R Page\",\"dwayne@fyrepower.com.au\",\"FIELD\",\"\",\"Technician\",\"\",\"\",\"\",\"\",\"\",\"\""));
        login = await db.Users.SingleAsync(u => u.Id == "dwayne");
        Assert.Equal(("Dwayne", "R Page", "dwayne@fyrepower.com.au", "DWAYNE@FYREPOWER.COM.AU"),
            (login.FirstName, login.LastName, login.UserName, login.NormalizedUserName));
    }

    [Fact]
    public async Task UsersImport_AssignsTasksImportedEarlier_ToTheTechnicianUptickNames()
    {
        using var db = DbContextFactory.Create();
        var site = new Site { Name = "Ezfurn", Client = new Client { Name = "Ezfurn" } };
        db.ClientTasks.AddRange(
            new ClientTask { Site = site, Client = site.Client, Title = "Imported", UptickTechnician = "Dwayne Page" },
            new ClientTask { Site = site, Client = site.Client, Title = "Someone else", UptickTechnician = "Not In Uptick" });
        await db.SaveChangesAsync();

        var result = await Import(db, Csv(UserHeader,
            "9,\"2021-02-18 21:27:16\",\"\",\"Dwayne Page (Field Tech)\",\"fyretech02@gmail.com\",\"FIELD\",\"\",\"Technician\",\"\",\"\",\"\",\"\",\"\",\"\""));

        var dwayne = await db.UptickUsers.SingleAsync();
        Assert.Equal(dwayne.Id, (await db.ClientTasks.SingleAsync(t => t.Title == "Imported")).AssignedToUptickUserId);
        Assert.Null((await db.ClientTasks.SingleAsync(t => t.Title == "Someone else")).AssignedToUptickUserId);
        Assert.Contains(result.Notes, n => n.StartsWith("1 imported task was assigned"));
    }

    [Fact]
    public async Task UsersImport_KeepsLoginEmail_WhenUpticksEmailBelongsToAnotherLogin()
    {
        using var db = DbContextFactory.Create();
        var uptick = new UptickUser { ExternalId = "9", Name = "Dwayne Page", Email = "old@example.com", LicenseType = "FIELD" };
        db.UptickUsers.Add(uptick);
        await db.SaveChangesAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = "dwayne", Email = "old@example.com", UserName = "old@example.com", UptickUserId = uptick.Id },
            new ApplicationUser { Id = "other", Email = "taken@example.com", UserName = "taken@example.com" });
        await db.SaveChangesAsync();

        var result = await Import(db, Csv(UserHeader,
            "9,\"2021-02-18 21:27:16\",\"\",\"Dwayne Page\",\"taken@example.com\",\"FIELD\",\"\",\"Technician\",\"\",\"\",\"\",\"\",\"\",\"\""));

        Assert.Contains(result.Notes, n => n.Contains("belongs to another FyreApp login"));
        Assert.Equal("old@example.com", (await db.Users.SingleAsync(u => u.Id == "dwayne")).Email);
    }

    [Theory]
    [InlineData("Wes Speedon (Field Tech)", "Wes", "Speedon")]
    [InlineData("Dwayne R Page", "Dwayne", "R Page")]
    [InlineData("FyrePower", "FyrePower", "")]
    [InlineData("(Office)", "(Office)", "")]
    public void UptickName_SplitsIntoFirstAndLast(string name, string first, string last) =>
        Assert.Equal((first, last), FyreApp.Services.UserManagement.UptickLoginSync.SplitName(name));
}
