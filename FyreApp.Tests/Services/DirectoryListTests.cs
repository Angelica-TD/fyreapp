using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Lists;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Lists;
using Xunit;

namespace FyreApp.Tests.Services;

// The Contractors, Suppliers and Users lists
public class DirectoryListTests
{
    [Fact]
    public async Task Contractors_ListActiveByDefault_StarredFirst()
    {
        using var db = DbContextFactory.Create();
        db.Contractors.AddRange(
            new Contractor { UptickId = 5, Name = "CR&K Kay Plumbing", Active = true },
            new Contractor { UptickId = 4, Name = "Rays Advantage Electrical", Active = true, Starred = true },
            new Contractor { UptickId = 6, Name = "Old Sparky", Active = false });
        await db.SaveChangesAsync();

        var list = new DirectoryListService(db);
        var active = await list.ContractorsAsync(new DirectoryFilter());
        // Starred first, then by name; the inactive one is hidden
        Assert.Equal(new[] { "Rays Advantage Electrical", "CR&K Kay Plumbing" }, active.Items.Select(c => c.Name));
        var any = await list.ContractorsAsync(new DirectoryFilter { Active = "any", Search = "sparky" });
        Assert.Equal("Old Sparky", Assert.Single(any.Items).Name);
    }

    [Fact]
    public async Task Suppliers_SearchByAbnIgnoringSpaces()
    {
        using var db = DbContextFactory.Create();
        db.Suppliers.AddRange(
            new Supplier { UptickId = 175, Name = "Auscoast Fire Services", BusinessNumber = "91 123 601 276", Active = true, AccountsContactName = "Jade", AccountsEmail = "ap@auscoastfire.com.au" },
            new Supplier { UptickId = 5, Name = "CALTEX CLEARING", Active = false });
        await db.SaveChangesAsync();

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
        var dwayne = new UptickUser { UptickId = 9, Name = "Dwayne Page", Email = "FyreTech02@gmail.com", LicenseType = "FIELD" };
        db.UptickUsers.AddRange(
            new UptickUser { UptickId = 13, Name = "Mermaid Central", Email = "graham@auadmin.com.au", LicenseType = "CUSTOMER" },
            dwayne,
            new UptickUser { UptickId = 3, Name = "FyrePower Team", Email = "admin@fyrepower.com.au", LicenseType = "DESK" });
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

    [Theory]
    [InlineData("Wes Speedon (Field Tech)", "Wes", "Speedon")]
    [InlineData("Dwayne R Page", "Dwayne", "R Page")]
    [InlineData("FyrePower", "FyrePower", "")]
    [InlineData("(Office)", "(Office)", "")]
    public void UptickName_SplitsIntoFirstAndLast(string name, string first, string last) =>
        Assert.Equal((first, last), FyreApp.Services.UserManagement.UptickLoginSync.SplitName(name));

    [Fact]
    public async Task UsersList_ShowsTechniciansAccreditations()
    {
        using var db = DbContextFactory.Create();
        var user = new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD" };
        db.UptickUsers.Add(user);
        db.Accreditations.Add(new Accreditation { UptickId = 67, Type = "Licence No.:", Number = "QBCC 15366855 | FPA 25023", Technician = "Dwayne Page", UptickUser = user });
        await db.SaveChangesAsync();

        var list = await new DirectoryListService(db).UptickUsersAsync(new DirectoryFilter { Active = "any" });

        Assert.Equal("QBCC 15366855 | FPA 25023", Assert.Single(Assert.Single(list.Items).Accreditations).Number);
    }

    [Fact]
    public async Task UsersList_ShowsSplitGroups()
    {
        using var db = DbContextFactory.Create();
        db.SecurityGroups.AddRange(new SecurityGroup { Name = "Technician" }, new SecurityGroup { Name = "Technician - Higher Permissions" });
        db.UptickUsers.Add(new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD", SecurityGroups = "Technician - Technician - Higher Permissions" });
        await db.SaveChangesAsync();

        var list = await new FyreApp.Services.Lists.DirectoryListService(db).UptickUsersAsync(new FyreApp.ViewModels.Lists.DirectoryFilter { Active = "any" });

        Assert.Equal(new[] { "Technician", "Technician - Higher Permissions" }, Assert.Single(list.Items).Groups);
    }

    [Theory]
    [InlineData("Technician - Technician - Higher Permissions", new[] { "Technician", "Technician - Higher Permissions" })]
    [InlineData("Office - Manager", new[] { "Office", "Manager" })]
    [InlineData("Client", new[] { "Client" })]
    [InlineData("Unknown - Office", new[] { "Unknown", "Office" })]
    public void SecurityGroups_SplitOnTheLongestKnownName(string joined, string[] expected) =>
        Assert.Equal(expected, UptickReferenceValues.SplitSecurityGroups(joined,
            new[] { "Technician", "Technician - Higher Permissions", "Office", "Manager", "Client" }));
}
