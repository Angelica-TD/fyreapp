using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Routines;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Routines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// The property page's Routines year grid (Uptick's property Routines tab)
public class PropertyRoutinesTests
{
    private static DateTime D(string date) => DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc);

    private static RoutineOccurrence O(Property site, string routine, string due, RoutineOccurrenceStatus status, string? group = null) =>
        new() { Property = site, Routine = routine, DueDate = D(due), Status = status, ServiceGroup = group };

    [Fact]
    public async Task ForProperty_GroupsRoutinesByServiceGroup_WithAMarkerInEachDueMonth()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Camajay Apartments", Client = new Client { Name = "Camajay" } };
        var other = new Property { Name = "Elsewhere", Client = new Client { Name = "Other" } };
        var level = new RoutineServiceLevel
        {
            Name = "Annual", DisplayCode = "A",
            RoutineServiceType = new RoutineServiceType { Name = "15 - Emergency Escape Lighting and Exit Signs", ServiceGroup = "Fire Detection (FIP, Exits) System Servicing" }
        };
        var exits = O(site, "15 - Emergency Escape Lighting and Exit Signs: Annual", "2026-08-31", RoutineOccurrenceStatus.Generated);
        exits.RoutineServiceLevel = level;
        db.RoutineOccurrences.AddRange(
            exits,
            O(site, "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", "2026-04-30", RoutineOccurrenceStatus.Complete, "Servicing - Portables & Fire Equipment"),
            O(site, "10 - Portable and Wheeled Fire Extinguishers: Annual", "2026-10-31", RoutineOccurrenceStatus.Pending, "Servicing - Portables & Fire Equipment"),
            O(site, "10 - Portable and Wheeled Fire Extinguishers: Six-monthly", "2026-10-31", RoutineOccurrenceStatus.Pending, "Servicing - Portables & Fire Equipment"),
            O(site, "09 - Fire Hose Reels: Annual", "2026-06-30", RoutineOccurrenceStatus.Cancelled, "Servicing - Portables & Fire Equipment"), // cancelled: not shown
            O(site, "10 - Portable and Wheeled Fire Extinguishers: Annual", "2027-10-31", RoutineOccurrenceStatus.Pending, "Servicing - Portables & Fire Equipment"), // next year
            O(other, "10 - Portable and Wheeled Fire Extinguishers: Annual", "2026-10-31", RoutineOccurrenceStatus.Pending, "Servicing - Portables & Fire Equipment")); // other property
        await db.SaveChangesAsync();

        var vm = await new RoutineService(db).ForPropertyAsync(site.Id, 2026);

        Assert.Equal(new[] { "Servicing - Portables & Fire Equipment", "Fire Detection (FIP, Exits) System Servicing" }, vm.Groups.Select(g => g.Name));
        var extinguishers = Assert.Single(vm.Groups[0].Rows);
        Assert.Equal("10 - Portable and Wheeled Fire Extinguishers", extinguishers.Routine);
        Assert.Equal("S", Assert.Single(extinguishers.Months[3]).Code); // April, six-monthly (no level linked: first letter)
        Assert.Equal(new[] { "Annual", "Six-monthly" }, extinguishers.Months[9].Select(m => m.Level).OrderBy(l => l));
        Assert.Equal(3, extinguishers.Months.Sum(m => m.Count));

        var marker = Assert.Single(Assert.Single(vm.Groups[1].Rows).Months[7]);
        Assert.Equal(("A", "Annual"), (marker.Code, marker.Level)); // from the linked Uptick level
    }

    [Fact]
    public async Task ForProperty_ShowsRoutineTasksInTheirDueMonth()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Camajay Apartments", Client = new Client { Name = "Camajay" } };
        var routineTask = new ClientTask { Title = "PM2026/08 Servicing", Client = site.Client, Property = site, DueDateUtc = D("2026-08-31"), Status = ClientTaskStatus.Completed };
        var adHoc = new ClientTask { Title = "Fix door", Client = site.Client, Property = site, DueDateUtc = D("2026-08-15") };
        var occurrence = O(site, "10 - Extinguishers: Annual", "2026-08-31", RoutineOccurrenceStatus.Complete);
        occurrence.ClientTask = routineTask;
        db.RoutineOccurrences.Add(occurrence);
        db.ClientTasks.Add(adHoc);
        await db.SaveChangesAsync();

        var vm = await new RoutineService(db).ForPropertyAsync(site.Id, 2026);

        var task = Assert.Single(vm.TasksByMonth[7]);
        Assert.Equal((routineTask.Id, true), (task.Id, task.Completed));
        Assert.All(vm.TasksByMonth.Where((_, i) => i != 7), m => Assert.Empty(m));
    }

    [Theory]
    [InlineData(RoutineOccurrenceStatus.Pending, "2026-11-30", null, RoutineMarkerState.Forecasted)]
    [InlineData(RoutineOccurrenceStatus.Generated, "2026-11-30", null, RoutineMarkerState.TaskCreated)]
    [InlineData(RoutineOccurrenceStatus.Complete, "2026-09-30", "2026-09-15", RoutineMarkerState.CompletedWithinTolerance)]
    [InlineData(RoutineOccurrenceStatus.Complete, "2026-09-30", "2026-10-02", RoutineMarkerState.CompletedOutsideTolerance)]
    [InlineData(RoutineOccurrenceStatus.Pending, "2026-09-30", null, RoutineMarkerState.NotCompletedOutsideTolerance)]
    [InlineData(RoutineOccurrenceStatus.Generated, "2026-09-30", null, RoutineMarkerState.NotCompletedOutsideTolerance)]
    public void MarkerState_FollowsUptickLegend(RoutineOccurrenceStatus status, string toleranceEnd, string? completed, RoutineMarkerState expected)
    {
        var today = new DateTime(2026, 10, 13);
        Assert.Equal(expected, RoutineService.MarkerState(status, D(toleranceEnd), completed == null ? null : D(completed), today));
    }

    [Fact]
    public async Task CompletingARoutineTask_CompletesItsRoutines()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Camajay Apartments", Client = new Client { Name = "Camajay" } };
        var task = new ClientTask { Title = "PM2026/10 Servicing", Client = site.Client, Property = site };
        var raised = O(site, "10 - Extinguishers: Annual", "2026-10-31", RoutineOccurrenceStatus.Generated);
        raised.ClientTask = task;
        var other = O(site, "09 - Fire Hose Reels: Annual", "2026-10-31", RoutineOccurrenceStatus.Pending);
        db.RoutineOccurrences.AddRange(raised, other);
        await db.SaveChangesAsync();

        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        await new ClientTaskService(db, users.Object, Mock.Of<IMaintenanceScheduleService>()).CompleteAsync(task.Id);

        Assert.Equal(RoutineOccurrenceStatus.Complete, raised.Status);
        Assert.NotNull(raised.CompletedDate);
        Assert.Equal(RoutineOccurrenceStatus.Pending, other.Status);
    }
}
