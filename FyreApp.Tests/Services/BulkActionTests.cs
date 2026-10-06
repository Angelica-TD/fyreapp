using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Assets;
using FyreApp.Services.Clients;
using FyreApp.Services.Lists;
using FyreApp.Services.Tasks;
using FyreApp.Services;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Assets;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// List pages' Edit actions and downloads
public class BulkActionTests
{
    private static async Task<(Site A, Site B)> SeedAsync(AppDbContext db)
    {
        var client = new Client { Name = "Sails Ministry", PrimaryContactName = "Russell McClue" };
        var a = new Site { Name = "The Glory Well Church", ExternalId = "P-2241", Status = "ACTIVE", Client = client };
        var b = new Site { Name = "Cold Front Air Conditioning", ExternalId = "P-1690", Status = "ACTIVE", Client = client };
        db.Assets.AddRange(
            new Asset { Name = "DCP AB(E) 4.5KG", Ref = "1", Location = "Kitchen", Site = a },
            new Asset { Name = "Fire Blanket", Ref = "2", Site = a },
            new Asset { Name = "Fire Hose Reel", Ref = "1", Site = b });
        await db.SaveChangesAsync();

        db.Defects.AddRange(
            new Defect { ExternalId = "54214", Site = a, Asset = db.Assets.Local.First(x => x.Name == "DCP AB(E) 4.5KG"), RemarkType = "00 - Asset Failed", SeverityLabel = "Non-conformance", Active = true, Description = "Expired" },
            new Defect { ExternalId = "54212", Site = a, Asset = db.Assets.Local.First(x => x.Name == "Fire Blanket"), RemarkType = "00 - Asset Failed", SeverityLabel = "Non-conformance", Active = true },
            new Defect { ExternalId = "54179", Site = b, Asset = db.Assets.Local.First(x => x.Name == "Fire Hose Reel"), RemarkType = "00 - Asset Failed", SeverityLabel = "Critical defect", Active = true });
        await db.SaveChangesAsync();
        return (a, b);
    }

    private static BulkSelection All => new(Array.Empty<int>(), AllMatching: true);

    [Fact]
    public async Task Properties_SetStatus_InactiveOnlyForInactive()
    {
        using var db = DbContextFactory.Create();
        var (a, b) = await SeedAsync(db);
        var svc = new PropertyListService(db);

        Assert.Equal(1, await svc.SetStatusAsync(new BulkSelection(new[] { a.Id }, false), new PropertyFilter(), "INACTIVE"));
        Assert.Equal(1, await svc.SetStatusAsync(new BulkSelection(new[] { b.Id }, false), new PropertyFilter(), "onhold"));

        Assert.Equal(("INACTIVE", false), (a.Status, a.Active));
        Assert.Equal(("ONHOLD", true), (b.Status, b.Active));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetStatusAsync(All, new PropertyFilter(), "GONE"));
    }

    [Fact]
    public async Task Properties_GenerateTasks_OnePerProperty()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        var created = await new PropertyListService(db).GenerateTasksAsync(All, new PropertyFilter(),
            new BulkTaskInput { Title = "Annual audit", Category = "I&T", DueDate = new DateTime(2026, 12, 15) }, "u1");

        Assert.Equal(2, created);
        Assert.All(await db.ClientTasks.ToListAsync(), t =>
            Assert.Equal(("Annual audit", "I&T", new DateTime(2026, 12, 15), "u1"), (t.Title, t.Category, t.DueDateUtc, t.CreatedByUserId)));
    }

    [Fact]
    public async Task Assets_SetInactive_AndTasksListTheSelectedAssets()
    {
        using var db = DbContextFactory.Create();
        var (a, _) = await SeedAsync(db);
        var svc = new AssetListService(db);
        var atA = await db.Assets.Where(x => x.SiteId == a.Id).Select(x => x.Id).ToListAsync();

        var created = await svc.GenerateTasksAsync(new BulkSelection(atA, false), new AssetFilter(), new BulkTaskInput(), null);
        var task = await db.ClientTasks.SingleAsync();
        Assert.Equal((1, "Asset works", a.Id), (created, task.Title, task.SiteId));
        Assert.Contains("- 1 - DCP AB(E) 4.5KG (Kitchen)", task.Description);
        Assert.Contains("- 2 - Fire Blanket", task.Description);

        Assert.Equal(2, await svc.SetActiveAsync(new BulkSelection(atA, false), new AssetFilter(), false));
        Assert.Equal(1, await db.Assets.CountAsync(x => x.IsActive));
    }

    [Fact]
    public async Task Remarks_RepairTasksPerProperty_AndResolve()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);
        var svc = new RemarkListService(db);
        var filter = new RemarkFilter { AssetActive = null, PropertyStatus = new() };

        var created = await svc.GenerateRepairTasksAsync(All, filter, new BulkTaskInput(), null);

        Assert.Equal(2, created);
        var tasks = await db.ClientTasks.ToListAsync();
        Assert.All(tasks, t => Assert.Equal(("Defect repairs", "Repair"), (t.Title, t.Category)));
        Assert.Contains(tasks, t => t.Description!.Contains("- D-54214 00 - Asset Failed (DCP AB(E) 4.5KG): Expired"));
        Assert.All(await db.Defects.ToListAsync(), d => Assert.Equal("Open", d.RepairTaskStatus));

        Assert.Equal(3, await svc.SetResolvedAsync(All, filter, resolved: true));
        Assert.All(await db.Defects.ToListAsync(), d => Assert.Equal((false, "Resolved"), (d.Active, d.Status)));
        // Resolved remarks drop out of the default (open) list
        Assert.Equal(0, (await svc.SearchAsync(filter)).Total);
    }

    [Fact]
    public async Task Clients_SetActive_ForAllMatchingSearch()
    {
        using var db = DbContextFactory.Create();
        db.Clients.AddRange(new Client { Name = "Thrifty Noosa" }, new Client { Name = "Thrifty Ipswich" }, new Client { Name = "Other" });
        await db.SaveChangesAsync();

        var changed = await new ClientService(db).SetActiveAsync(All, "thrifty", true, makeActive: false);

        Assert.Equal(2, changed);
        Assert.Equal(new[] { "Other" }, await db.Clients.Where(c => c.Active).Select(c => c.Name).ToListAsync());
    }

    [Fact]
    public async Task Tasks_Complete_GoesThroughTheTaskService_OtherStatusesSetDirectly()
    {
        using var db = DbContextFactory.Create();
        var (a, _) = await SeedAsync(db);
        db.ClientTasks.AddRange(
            new ClientTask { Title = "One", ClientId = a.ClientId, SiteId = a.Id },
            new ClientTask { Title = "Two", ClientId = a.ClientId, SiteId = a.Id });
        await db.SaveChangesAsync();
        var taskService = new Mock<IClientTaskService>();
        var svc = new TaskListService(db, taskService.Object, null!);

        Assert.Equal(2, await svc.SetStatusAsync(All, new TaskFilter(), ClientTaskStatus.Completed));
        taskService.Verify(s => s.CompleteAsync(It.IsAny<int>()), Times.Exactly(2));

        Assert.Equal(2, await svc.SetStatusAsync(All, new TaskFilter(), ClientTaskStatus.Cancelled));
        Assert.All(await db.ClientTasks.ToListAsync(), t => Assert.Equal((ClientTaskStatus.Cancelled, false), (t.Status, t.IsActive)));
    }

    [Fact]
    public async Task Downloads_HaveAHeaderAndOneRowPerMatch()
    {
        using var db = DbContextFactory.Create();
        await SeedAsync(db);

        string Text(byte[] csv) => Encoding.UTF8.GetString(csv).TrimStart('﻿');
        var properties = Text(await new PropertyListService(db).CsvAsync(new PropertyFilter())).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var remarks = Text(await new RemarkListService(db).CsvAsync(new RemarkFilter { AssetActive = null })).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, properties.Length);
        Assert.StartsWith("Ref,Status,Created,Property", properties[0]);
        Assert.Equal(4, remarks.Length);
        Assert.Contains(remarks, l => l.StartsWith("D-54179,Critical defect"));
    }
}
