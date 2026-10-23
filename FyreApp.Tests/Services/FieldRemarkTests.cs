using System.Text;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.Services.Files;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Field;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Remarks in the field app: raised on an asset with photos as found, resolved with photos once fixed
public class FieldRemarkTests
{
    private sealed class MemoryStore(bool configured = true) : IFileStore
    {
        public readonly Dictionary<string, (byte[] Bytes, string? Type)> Files = new();
        public bool IsConfigured => configured;
        public string Description => "memory";

        public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Files[key] = (copy.ToArray(), contentType);
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var f) ? new MemoryStream(f.Bytes) : null);
    }

    private sealed record World(AppDbContext Db, ClientTask Task, Asset Extinguisher, RemarkType Pressure, RemarkType Exit);

    private static async Task<World> SetupAsync()
    {
        var db = DbContextFactory.Create();
        var site = new Property { Name = "Gemvale Glades", Client = new Client { Name = "Gemvale" } };
        var fe = new AssetType { Name = "Portable Fire Extinguisher" };
        var exit = new AssetType { Name = "Exit Light" };
        var extinguisher = new Asset { Name = "Extinguisher", Ref = "FE 3", Location = "Level 1 stairwell", Property = site, AssetTypes = [fe] };
        var pressure = new RemarkType { Label = "1.05 - Pressure gauge in the red", AssetType = fe, SeverityLabel = "Critical defect", Resolution = "Replace extinguisher" };
        var exitType = new RemarkType { Label = "0.01 - Exit light not illuminated", AssetType = exit, SeverityLabel = "Non-conformance" };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "Six-monthly I&T", Ref = "T-1" };
        db.AddRange(task, extinguisher, pressure, exitType);
        db.ServiceTasks.Add(new ServiceTask { ClientTask = task, Asset = extinguisher, Routine = "10 - Portable Fire Extinguishers: Six-monthly" });
        await db.SaveChangesAsync();
        return new World(db, task, extinguisher, pressure, exitType);
    }

    private static IFormFile Photo(string name = "IMG_0001.jpg", string type = "image/jpeg")
    {
        var bytes = Encoding.UTF8.GetBytes("not really a jpeg");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photos", name) { Headers = new HeaderDictionary(), ContentType = type };
    }

    [Fact]
    public async Task RaisingARemark_OnAnAsset_KeepsItsPhotosAsFound_AndShowsItOnTheFieldPage()
    {
        var w = await SetupAsync();
        var store = new MemoryStore();
        var remarks = new FieldRemarkService(w.Db, store);

        var saved = await remarks.RaiseAsync(w.Task.Id,
            new RaiseRemarkInput { AssetId = w.Extinguisher.Id, RemarkTypeId = w.Pressure.Id, Location = "Level 1 stairwell" }, [Photo()]);

        Assert.Equal(FieldOutcome.Done, saved.Result.Outcome);
        Assert.Empty(saved.PhotoProblems!);
        var remark = await w.Db.Remarks.SingleAsync();
        Assert.Equal((w.Extinguisher.Id, w.Task.Id, "Needs Quoting", true), (remark.AssetId, remark.RaisedOnTaskId, remark.Status, remark.Active));
        Assert.Equal(("1.05 - Pressure gauge in the red", 10, "Critical defect"), (remark.RemarkType, remark.Severity, remark.SeverityLabel));
        Assert.Equal("1.05 - Pressure gauge in the red", remark.Description);

        var photo = await w.Db.UptickFiles.SingleAsync();
        Assert.Equal((FyreAppFiles.Source, "remarks", remark.Id, "open", "image/jpeg"), (photo.Source, photo.RecordType, photo.RecordId!.Value, photo.PhotoType, photo.ContentType));
        Assert.True(store.Files.ContainsKey(photo.StorageKey!));
        Assert.StartsWith($"fyreapp/remarks/{remark.Id}/", photo.StorageKey);

        // The remark's page shows the photo as found
        var panel = await UptickFilesViewComponent.LoadAsync(w.Db, "remarks", null, remark.Id);
        Assert.Equal("open", Assert.Single(panel.Photos).PhotoType);

        // and the field page lists it under the asset
        var page = (await new FieldService(w.Db).TaskAsync(w.Task.Id, null))!;
        var listed = Assert.Single(Assert.Single(page.Lines).Remarks);
        Assert.Equal((remark.Id, true), (listed.Id, listed.RaisedHere));
        Assert.Empty(page.OtherRemarks);
    }

    [Fact]
    public async Task ARemark_NeedsATypeOrADescription_AndAnAssetAtTheProperty()
    {
        var w = await SetupAsync();
        var elsewhere = new Asset { Name = "Hose reel", Property = new Property { Name = "Elsewhere", Client = w.Task.Client } };
        w.Db.Assets.Add(elsewhere);
        await w.Db.SaveChangesAsync();
        var remarks = new FieldRemarkService(w.Db, new MemoryStore());

        var empty = await remarks.RaiseAsync(w.Task.Id, new RaiseRemarkInput { AssetId = w.Extinguisher.Id }, []);
        var wrongAsset = await remarks.RaiseAsync(w.Task.Id, new RaiseRemarkInput { AssetId = elsewhere.Id, Description = "Leaking" }, []);
        var badSeverity = await remarks.RaiseAsync(w.Task.Id, new RaiseRemarkInput { Description = "Leaking", Severity = 4 }, []);
        var onProperty = await remarks.RaiseAsync(w.Task.Id, new RaiseRemarkInput { Description = "Fire door wedged open", Severity = 2 }, []);

        Assert.Equal(FieldOutcome.Invalid, empty.Result.Outcome);
        Assert.Equal(FieldOutcome.NotFound, wrongAsset.Result.Outcome);
        Assert.Equal(FieldOutcome.Invalid, badSeverity.Result.Outcome);
        Assert.Equal(FieldOutcome.Done, onProperty.Result.Outcome);
        var remark = await w.Db.Remarks.SingleAsync();
        Assert.Equal((null, "Fire door wedged open", "Non-conformance"), (remark.AssetId, remark.Description, remark.SeverityLabel));

        var page = (await new FieldService(w.Db).TaskAsync(w.Task.Id, null))!;
        Assert.Equal(remark.Id, Assert.Single(page.OtherRemarks).Id);
    }

    [Fact]
    public async Task PhotosThatCantBeKept_AreReported_ButTheRemarkIsRaised()
    {
        var w = await SetupAsync();

        var noStorage = await new FieldRemarkService(w.Db, new MemoryStore(configured: false))
            .RaiseAsync(w.Task.Id, new RaiseRemarkInput { Description = "Leaking" }, [Photo()]);
        var notAPhoto = await new FieldRemarkService(w.Db, new MemoryStore())
            .RaiseAsync(w.Task.Id, new RaiseRemarkInput { Description = "Cracked" }, [Photo("notes.html", "text/html")]);

        Assert.Equal(FieldOutcome.Done, noStorage.Result.Outcome);
        Assert.Contains("file storage isn't set up", Assert.Single(noStorage.PhotoProblems!));
        Assert.Contains("isn't a photo", Assert.Single(notAPhoto.PhotoProblems!));
        Assert.Equal(2, await w.Db.Remarks.CountAsync());
        Assert.Empty(w.Db.UptickFiles);
    }

    [Fact]
    public async Task TheRaiseForm_OffersTheAssetsOwnRemarkTypesFirst()
    {
        var w = await SetupAsync();

        var vm = (await new FieldRemarkService(w.Db, new MemoryStore()).RaiseFormAsync(w.Task.Id, w.Extinguisher.Id))!;

        Assert.Equal(new[] { (w.Pressure.Id, true), (w.Exit.Id, false) }, vm.Types.Select(t => (t.Id, t.ForAsset)));
        Assert.Equal("Level 1 stairwell", vm.Input.Location);
    }

    [Fact]
    public async Task Resolving_ClosesTheRemark_WithPhotosOnceFixed()
    {
        var w = await SetupAsync();
        var store = new MemoryStore();
        var remarks = new FieldRemarkService(w.Db, store);
        var raised = await remarks.RaiseAsync(w.Task.Id, new RaiseRemarkInput { AssetId = w.Extinguisher.Id, RemarkTypeId = w.Pressure.Id }, []);
        var id = raised.RemarkId!.Value;

        var form = (await remarks.ResolveFormAsync(w.Task.Id, id))!;
        var noResolution = await remarks.ResolveAsync(w.Task.Id, id, " ", []);
        var resolved = await remarks.ResolveAsync(w.Task.Id, id, "Replaced extinguisher", [Photo("after.jpg")]);
        var again = await remarks.ResolveAsync(w.Task.Id, id, "Replaced extinguisher", []);

        Assert.Equal("Replace extinguisher", form.Resolution);
        Assert.Equal(FieldOutcome.Invalid, noResolution.Result.Outcome);
        Assert.Equal(FieldOutcome.Done, resolved.Result.Outcome);
        Assert.Equal(FieldOutcome.Invalid, again.Result.Outcome);
        var remark = await w.Db.Remarks.SingleAsync();
        Assert.Equal((false, "Resolved", "Replaced extinguisher"), (remark.Active, remark.Status, remark.Resolution));
        Assert.NotNull(remark.InactiveUtc);
        Assert.Equal("fixed", (await w.Db.UptickFiles.SingleAsync()).PhotoType);
        Assert.Null(await remarks.ResolveFormAsync(w.Task.Id, id));
        Assert.Empty(Assert.Single((await new FieldService(w.Db).TaskAsync(w.Task.Id, null))!.Lines).Remarks);
    }

    [Fact]
    public async Task AClosedTask_TakesNoRemarks()
    {
        var w = await SetupAsync();
        w.Task.Status = ClientTaskStatus.Cancelled;
        await w.Db.SaveChangesAsync();

        var saved = await new FieldRemarkService(w.Db, new MemoryStore()).RaiseAsync(w.Task.Id, new RaiseRemarkInput { Description = "Leaking" }, []);

        Assert.Equal(FieldOutcome.Invalid, saved.Result.Outcome);
        Assert.Empty(w.Db.Remarks);
    }
}
