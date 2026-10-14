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

    private static async Task<Site> SeedAsync(AppDbContext db)
    {
        var site = new Site { Name = "Ezfurn", ExternalId = "P-0391", Status = "ACTIVE", Client = new Client { Name = "Ezfurn" } };
        var other = new Site { Name = "Kamber", ExternalId = "P-3330", Status = "ACTIVE", Client = new Client { Name = "Kamber" } };
        db.RoutineOccurrences.AddRange(
            new RoutineOccurrence { Site = site, Routine = "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", ServiceGroup = "Servicing - Portables & Fire Equipment", DueDate = Day(2026, 11, 30) },
            new RoutineOccurrence { Site = other, Routine = "11 - Fire Blankets: Six-monthly", ServiceGroup = "Servicing - Portables & Fire Equipment", DueDate = Day(2026, 11, 30) },
            new RoutineOccurrence { Site = site, Routine = "06 - Fire Detection (Fire Panels): Monthly", ServiceGroup = "Fire Detection", DueDate = Day(2026, 12, 31) });
        // Already in hand: imported from Uptick with its technician, and one made in FyreApp
        db.ClientTasks.AddRange(
            new ClientTask { Site = site, Client = site.Client, Title = "PM2026/11 Imported", Category = "I&T", DueDateUtc = Day(2026, 11, 30), UptickTechnician = "Dwayne Page" },
            new ClientTask { Site = site, Client = site.Client, Title = "Repair", Category = "Repair", DueDateUtc = Day(2026, 11, 15) });
        await db.SaveChangesAsync();
        return site;
    }

    private static Mock<UserManager<ApplicationUser>> Users(params ApplicationUser[] techs)
    {
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        foreach (var t in techs)
        {
            users.Setup(u => u.FindByIdAsync(t.Id)).ReturnsAsync(t);
            users.Setup(u => u.IsInRoleAsync(t, "Tech")).ReturnsAsync(true);
        }
        return users;
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

        var wes = new ApplicationUser { Id = "wes", FirstName = "Wes", LastName = "Speedon" };
        db.Users.Add(wes);
        await db.SaveChangesAsync();
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>(), Users(wes).Object);

        // Tasks list: only the two generated tasks (the imported one is Uptick's Dwayne's; the repair isn't I&T)
        var (total, _, items, _) = await svc.SearchAsync(NovemberToAssign);
        Assert.Equal(2, total);
        Assert.All(items, i => Assert.StartsWith("PM2026/11", i.Title));
        Assert.All(items, i => Assert.Null(i.AssignedToUserId));

        // Edit → Assign technician to all matching
        Assert.Equal(2, await svc.AssignAsync(new BulkSelection(Array.Empty<int>(), AllMatching: true), NovemberToAssign, "wes"));

        Assert.Equal(0, (await svc.SearchAsync(NovemberToAssign)).Total);
        var (_, _, wesTasks, _) = await svc.SearchAsync(new TaskFilter { Technician = "wes" });
        Assert.Equal(2, wesTasks.Count);
        Assert.All(wesTasks, i => Assert.Equal("Wes Speedon", i.AssignedToName));
    }

    [Fact]
    public async Task OneTask_CanBeAssigned_AndNotToSomeoneWhoIsntATechnician()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedAsync(db);
        var task = await db.ClientTasks.SingleAsync(t => t.Title == "Repair");
        var wes = new ApplicationUser { Id = "wes", FirstName = "Wes", LastName = "Speedon" };
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>(), Users(wes).Object);
        var justThis = new BulkSelection(new[] { task.Id }, AllMatching: false);

        Assert.Equal(1, await svc.AssignAsync(justThis, new TaskFilter(), "wes"));
        Assert.Null(await svc.AssignAsync(justThis, new TaskFilter(), "office-user"));

        Assert.Equal("wes", (await db.ClientTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).AssignedToUserId);
        Assert.Equal(1, await db.ClientTasks.CountAsync(t => t.AssignedToUserId != null));
    }

    [Fact]
    public async Task DueFilter_IncludesBothEndDays()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var svc = new TaskListService(db, Mock.Of<IClientTaskService>(), null!);

        var (total, _, _, _) = await svc.SearchAsync(new TaskFilter { DueFrom = new DateTime(2026, 11, 15), DueTo = new DateTime(2026, 11, 30) });
        Assert.Equal(2, total);
        Assert.Equal(1, (await svc.SearchAsync(new TaskFilter { DueTo = new DateTime(2026, 11, 15) })).Total);
    }
}
