using FyreApp.Models;
using FyreApp.Services.Tasks;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Tasks;
using Xunit;

namespace FyreApp.Tests.Services;

// The task page, laid out like Uptick's (Task and Work tabs)
public class TaskPageTests
{
    [Fact]
    public async Task ImportedTask_ShowsUpticksNotesTagsAndRepairs()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Gemvale Glades CTS 38276", ExternalId = "P-1512", Client = new Client { Name = "Gemvale glades CTS 38276" } };
        var dwayne = new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD" };
        var customer = new UptickUser { Name = "Mermaid Central", LicenseType = "CUSTOMER" };
        var task = new ClientTask
        {
            Property = site, Client = site.Client, Title = "Q-2790 Works", Ref = "T-46748", ExternalId = "46748", Category = "Repair",
            Description = "Prepare\n\nQuote to replace failed items as per service report.", AssignedTo = dwayne,
            UptickData = """{"Description":"Prepare","Scope of works":"Quote to replace failed items as per service report.","Status":"READY","Tags":"Parts Required, Urgent","Invoice Note":"As per accepted defect quote Q-2790."}"""
        };
        db.AddRange(task, customer);
        db.Remarks.Add(new Remark { Property = site, Asset = new Asset { Name = "1 - HYD Pillar Landing Valve", Property = site }, RepairTask = task, Description = "Hydrant valve" });
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "work"))!;

        Assert.Equal("work", vm.Tab);
        Assert.Equal(("READY", "Prepare"), (vm.StatusLabel, vm.Subtitle));
        Assert.Equal("Quote to replace failed items as per service report.", vm.ScopeOfWorks);
        Assert.Equal(new[] { "Parts Required", "Urgent" }, vm.Tags);
        Assert.Equal("As per accepted defect quote Q-2790.", vm.U("Invoice Note"));
        Assert.Null(vm.U("Technician Note"));
        Assert.Equal("1 - HYD Pillar Landing Valve", Assert.Single(vm.Repairs).Asset!.Name);
        // Client portal users can't be assigned
        Assert.Equal(new[] { "Dwayne Page" }, vm.Techs.Select(x => x.Name));
    }

    [Fact]
    public async Task FyreAppTask_UsesItsOwnStatusAndDescription_AndAnUnknownTabIsTheTaskTab()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Ezfurn", Client = new Client { Name = "Ezfurn" } };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "Replace exit light", Description = "Exit light over stair 2 out.", Status = ClientTaskStatus.InProgress };
        db.ClientTasks.Add(task);
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, "nonsense"))!;

        Assert.Equal(("task", "IN PROGRESS"), (vm.Tab, vm.StatusLabel));
        Assert.Equal("Exit light over stair 2 out.", vm.ScopeOfWorks);
        Assert.Empty(vm.Tags);
        Assert.Null(await new TaskPageService(db).LoadAsync(999, null));
    }

    [Theory]
    [InlineData("Dwayne Page", "DP")]
    [InlineData("Wes Speedon (Field Tech)", "WS")]
    [InlineData("FyrePower Team", "FT")]
    [InlineData("Admin", "AD")]
    [InlineData(null, "?")]
    public void Initials_AsUptickShowsThem(string? name, string expected) =>
        Assert.Equal(expected, TaskPageVm.Initials(name));
}
