using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services;
using FyreApp.Services.Routines;
using FyreApp.Services.Tasks;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Lists;
using FyreApp.ViewModels.Routines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// An admin's month of routine work: Routines (due in November) → Generate tasks → Tasks list filtered to
// November's unassigned I&T tasks → assign each, or all at once
public class TaskAssignFlowTests
{
    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<Property> SeedAsync(AppDbContext db)
    {
        var site = new Property { Name = "Ezfurn", ExternalId = "P-0391", Status = "ACTIVE", Client = new Client { Name = "Ezfurn" } };
        var other = new Property { Name = "Kamber", ExternalId = "P-3330", Status = "ACTIVE", Client = new Client { Name = "Kamber" } };
        db.RoutineOccurrences.AddRange(
            new RoutineOccurrence { Property = site, Routine = "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", ServiceGroup = "Servicing - Portables & Fire Equipment", DueDate = Day(2026, 11, 30) },
            new RoutineOccurrence { Property = other, Routine = "11 - Fire Blankets: Six-monthly", ServiceGroup = "Servicing - Portables & Fire Equipment", DueDate = Day(2026, 11, 30) },
            new RoutineOccurrence { Property = site, Routine = "06 - Fire Detection (Fire Panels): Monthly", ServiceGroup = "Fire Detection", DueDate = Day(2026, 12, 31) });
        // Already in hand: imported from Uptick with its technician, and one made in FyreApp
        db.ClientTasks.AddRange(
            new ClientTask { Property = site, Client = site.Client, Title = "PM2026/11 Imported", Category = "I&T", DueDateUtc = Day(2026, 11, 30), UptickTechnician = "Dwayne Page" },
            new ClientTask { Property = site, Client = site.Client, Title = "Repair", Category = "Repair", DueDateUtc = Day(2026, 11, 15) });
        await db.SaveChangesAsync();
        return site;
    }

    // Technicians are Uptick users
    private static async Task<UptickUser> TechAsync(AppDbContext db, string name, string licence = "FIELD")
    {
        var user = new UptickUser { Name = name, LicenseType = licence };
        db.UptickUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // What the Routines page links to after generating: /Task?category=I%26T&technician=none&dueFrom=…&dueTo=…
    private static TaskFilter NovemberToAssign => new()
    {
        Category = new() { "I&T" }, Technician = "none", DueFrom = new DateTime(2026, 11, 1), DueTo = new DateTime(2026, 11, 30)
    };

    [Fact]
    public async Task GeneratedNovemberTasks_AreTheOnesListedToAssign_AndCanBeAssignedTogether()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        // Routines → due this month (November) → select all → Generate tasks
        var routines = new RoutineFilter { ClientActive = null, PropertyStatus = new(), Status = new(), DueFrom = new DateTime(2026, 11, 1), DueTo = new DateTime(2026, 11, 30) };
        var generated = await new RoutineService(db).GenerateTasksAsync(Array.Empty<int>(), allMatching: true, routines, "admin");
        Assert.Equal(2, generated.TasksCreated);

        var wes = await TechAsync(db, "Wes Speedon (Field Tech)");
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>());

        // Tasks list: only the two generated tasks (the imported one is Uptick's Dwayne's; the repair isn't I&T)
        var (total, _, items, _) = await svc.SearchAsync(NovemberToAssign);
        Assert.Equal(2, total);
        Assert.All(items, i => Assert.StartsWith("PM2026/11", i.Title));
        Assert.All(items, i => Assert.Null(i.AssignedToId));

        // Edit → Assign technician to all matching
        Assert.Equal(2, await svc.AssignAsync(new BulkSelection(Array.Empty<int>(), AllMatching: true), NovemberToAssign, wes.Id));

        Assert.Equal(0, (await svc.SearchAsync(NovemberToAssign)).Total);
        var (_, _, wesTasks, _) = await svc.SearchAsync(new TaskFilter { Technician = wes.Id.ToString() });
        Assert.Equal(2, wesTasks.Count);
        Assert.All(wesTasks, i => Assert.Equal("Wes Speedon (Field Tech)", i.AssignedToName));
    }

    [Fact]
    public async Task UpticksTechnician_IsOnlyShownWhenItIsSomeoneElse()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var dwayne = await TechAsync(db, "Dwayne Page (Field Tech)");
        var wes = await TechAsync(db, "Wes Speedon");
        var imported = await db.ClientTasks.SingleAsync(t => t.UptickTechnician != null);
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>());
        var justThis = new BulkSelection(new[] { imported.Id }, AllMatching: false);

        await svc.AssignAsync(justThis, new TaskFilter(), dwayne.Id);
        Assert.Null(Assert.Single((await svc.SearchAsync(new TaskFilter { Technician = dwayne.Id.ToString() })).Items).UptickTechnician);

        await svc.AssignAsync(justThis, new TaskFilter(), wes.Id);
        Assert.Equal("Dwayne Page", Assert.Single((await svc.SearchAsync(new TaskFilter { Technician = wes.Id.ToString() })).Items).UptickTechnician);
    }

    [Fact]
    public async Task OneTask_CanBeAssigned_AndNotToSomeoneWhoIsntATechnician()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);
        var task = await db.ClientTasks.SingleAsync(t => t.Title == "Repair");
        var wes = await TechAsync(db, "Wes Speedon");
        var client = await TechAsync(db, "Mermaid Central", "CUSTOMER");
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>());
        var justThis = new BulkSelection(new[] { task.Id }, AllMatching: false);

        Assert.Equal(1, await svc.AssignAsync(justThis, new TaskFilter(), wes.Id));
        Assert.Null(await svc.AssignAsync(justThis, new TaskFilter(), client.Id));
        Assert.Null(await svc.AssignAsync(justThis, new TaskFilter(), 9999));

        Assert.Equal(wes.Id, (await db.ClientTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).AssignedToUptickUserId);
        Assert.Equal(1, await db.ClientTasks.CountAsync(t => t.AssignedToUptickUserId != null));
    }

    [Fact]
    public async Task DueFilter_IncludesBothEndDays()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>());

        var (total, _, _, _) = await svc.SearchAsync(new TaskFilter { DueFrom = new DateTime(2026, 11, 15), DueTo = new DateTime(2026, 11, 30) });
        Assert.Equal(2, total);
        Assert.Equal(1, (await svc.SearchAsync(new TaskFilter { DueTo = new DateTime(2026, 11, 15) })).Total);
    }
}
