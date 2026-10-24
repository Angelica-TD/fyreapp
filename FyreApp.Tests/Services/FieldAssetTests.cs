using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.Services.Files;
using FyreApp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// An asset's page in the field app: its result on the job, its remarks, photos and history
public class FieldAssetTests
{
    private sealed class MemoryStore : IFileStore
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public bool IsConfigured => true;
        public string Description => "memory";

        public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Files[key] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var f) ? new MemoryStream(f) : null);
    }

    private sealed record World(AppDbContext Db, UptickUser Me, ClientTask Task, ClientTask Earlier, Asset Extinguisher, Asset Hose, Asset Elsewhere);

    private static async Task<World> SetupAsync()
    {
        var db = DbContextFactory.Create();
        var me = new UptickUser { Name = "Dwayne Page" };
        var site = new Property { Name = "Gemvale Glades", Client = new Client { Name = "Gemvale" } };
        var other = new Property { Name = "Elsewhere", Client = site.Client };
        var extinguisher = new Asset { Name = "Extinguisher", Ref = "001", Location = "Lobby", Make = "Chubb", Property = site };
        var hose = new Asset { Name = "Hose reel", Ref = "002", Property = site };
        var elsewhere = new Asset { Name = "Blanket", Property = other };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "Six-monthly I&T", Ref = "T-2", AssignedTo = me };
        var earlier = new ClientTask { Property = site, Client = site.Client, Title = "Annual I&T", Ref = "T-1", Status = ClientTaskStatus.Completed, IsActive = false };
        db.AddRange(task, earlier, extinguisher, hose, elsewhere, me);
        db.ServiceTasks.AddRange(
            new ServiceTask { ClientTask = task, Asset = extinguisher, Routine = "Six-monthly" },
            new ServiceTask { ClientTask = earlier, Asset = extinguisher, Routine = "Annual", Result = "Fail", Notes = "Gauge in red", PerformedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), Technician = me });
        db.Remarks.AddRange(
            new Remark { Property = site, Asset = extinguisher, Description = "Missing tag", Active = true },
            new Remark { Property = site, Asset = extinguisher, Description = "Gauge in red", Active = false, Status = "Resolved", Resolution = "Replaced" });
        db.TimelineEvents.Add(new TimelineEvent { Asset = extinguisher, Author = "Office", Text = "Moved to the lobby" });
        await db.SaveChangesAsync();
        return new World(db, me, task, earlier, extinguisher, hose, elsewhere);
    }

    private static FieldAssetService Service(AppDbContext db, IFileStore? store = null) =>
        new(db, new FieldService(db), store ?? new MemoryStore());

    private static IFormFile Photo()
    {
        var bytes = Encoding.UTF8.GetBytes("jpeg bytes");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photos", "IMG_0001.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
    }

    [Fact]
    public async Task AnAsset_ShowsItsLineOnTheJob_RemarksAndHistory()
    {
        var w = await SetupAsync();

        var page = await Service(w.Db).AssetAsync(w.Task.Id, w.Extinguisher.Id, w.Me.Id);

        Assert.NotNull(page);
        Assert.True(page!.CanWork);
        Assert.Equal("Chubb", page.Asset.Make);
        Assert.Equal(new[] { "Six-monthly" }, page.Lines.Select(l => l.Routine));
        Assert.Equal(new[] { "Missing tag" }, page.OpenRemarks.Select(r => r.Description));
        Assert.Equal(new[] { "Replaced" }, page.PastRemarks.Select(r => r.Resolution));
        var service = Assert.Single(page.Service);
        Assert.Equal(("T-1", "Fail", "Gauge in red", "Dwayne Page"), (service.TaskRef, service.Result, service.Notes, service.By));
        Assert.Equal("Moved to the lobby", Assert.Single(page.Timeline).Text);
    }

    [Fact]
    public async Task AnAssetNotOnTheJob_OpensWithNothingToRecord_OneElsewhereDoesnt()
    {
        var w = await SetupAsync();

        var hose = await Service(w.Db).AssetAsync(w.Task.Id, w.Hose.Id, w.Me.Id);
        Assert.Empty(hose!.Lines);
        Assert.Empty(hose.OpenRemarks);

        Assert.Null(await Service(w.Db).AssetAsync(w.Task.Id, w.Elsewhere.Id, w.Me.Id));
    }

    [Fact]
    public async Task Photos_AreKeptOnTheAsset()
    {
        var w = await SetupAsync();
        var store = new MemoryStore();

        var (result, problems) = await Service(w.Db, store).AddPhotosAsync(w.Task.Id, w.Extinguisher.Id, [Photo()]);

        Assert.Equal(FieldOutcome.Done, result.Outcome);
        Assert.Empty(problems);
        var file = await w.Db.UptickFiles.SingleAsync();
        Assert.Equal(("assets", w.Extinguisher.Id, FyreAppFiles.Source, FieldAssetService.PhotoType), (file.RecordType, file.RecordId, file.Source, file.PhotoType));
        Assert.Single(store.Files);

        // Nothing picked, an asset elsewhere, a closed job
        Assert.Equal(FieldOutcome.Invalid, (await Service(w.Db).AddPhotosAsync(w.Task.Id, w.Extinguisher.Id, [])).Result.Outcome);
        Assert.Equal(FieldOutcome.NotFound, (await Service(w.Db).AddPhotosAsync(w.Task.Id, w.Elsewhere.Id, [Photo()])).Result.Outcome);
        Assert.Equal(FieldOutcome.Invalid, (await Service(w.Db).AddPhotosAsync(w.Earlier.Id, w.Extinguisher.Id, [Photo()])).Result.Outcome);
    }
}
