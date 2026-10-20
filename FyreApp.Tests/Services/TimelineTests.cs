using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Assets;
using FyreApp.Services.Timeline;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Assets;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Property and asset Timelines, as Uptick keeps them: "Access note was changed to ...", by whom and when
public class TimelineTests
{
    [Fact]
    public async Task AssetUpdateForm_SavesFields_AndLogsEachChangeByTheEditor()
    {
        using var db = DbContextFactory.Create();
        var extinguisher = new AssetType { Name = "Fire Extinguisher" };
        var variant = new AssetTypeVariant { AssetType = extinguisher, Name = "DCP AB(E) 2.5KG" };
        db.AssetTypeVariants.Add(variant);
        var asset = new Asset
        {
            Name = "002 - DCP", Location = "Upstairs", IsActive = true, AssetTypes = { extinguisher },
            Property = new Property { Name = "Kamber", Client = new Client { Name = "Kamber" } }
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var sut = new AssetPageService(db, new TimelineService(db));

        var form = (await sut.EditFormAsync(asset.Id))!;
        form.Location = "Upstairs middle office";
        form.AssetTypeVariantId = variant.Id;
        form.BaseDate = new DateTime(2026, 8, 1);
        Assert.True(await sut.SaveAsync(form, "Wes Speedon"));

        var saved = await db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal(("Upstairs middle office", "DCP AB(E) 2.5KG", variant.Id), (saved.Location, saved.Variant, saved.AssetTypeVariantId));
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), saved.BaseDate);

        var entries = await db.TimelineEvents.Where(e => e.AssetId == asset.Id).OrderBy(e => e.Field).ToListAsync();
        Assert.Equal(new[] { "Base date", "Location", "Variant" }, entries.Select(e => e.Field));
        Assert.All(entries, e => Assert.Equal("Wes Speedon", e.Author));
    }

    [Fact]
    public async Task AssetUpdateForm_KeepsAnImportedVariantWithNoRecord()
    {
        using var db = DbContextFactory.Create();
        var asset = new Asset
        {
            Name = "001 - DCP", Variant = "DCP AB(E) 4.5KG", AssetTypes = { new AssetType { Name = "Fire Extinguisher" } },
            Property = new Property { Name = "Kamber", Client = new Client { Name = "Kamber" } }
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var sut = new AssetPageService(db, new TimelineService(db));

        var form = (await sut.EditFormAsync(asset.Id))!;
        form.Location = "Right side of roller door";
        await sut.SaveAsync(form, "Wes Speedon");

        Assert.Equal("DCP AB(E) 4.5KG", (await db.Assets.AsNoTracking().SingleAsync()).Variant);
        Assert.Equal("Location", (await db.TimelineEvents.SingleAsync()).Field);
    }

    [Fact]
    public void Changes_IgnoreBlankVersusMissing_AndLineEndings()
    {
        var before = new (string, string?)[] { ("Access note", "a\r\nb"), ("Internal note", ""), ("Name", "X") };
        var after = new (string, string?)[] { ("Access note", "a\nb"), ("Internal note", null), ("Name", "Y") };

        var change = Assert.Single(TimelineLog.Changes(before, after, "Uptick", DateTime.UtcNow));
        Assert.Equal(("Name", "Y"), (change.Field, change.Value));
    }

    [Theory]
    [InlineData(2026, 10, 13, 14, 26, "13th Oct 2026 2:26pm")]
    [InlineData(2026, 9, 1, 9, 5, "1st Sep 2026 9:05am")]
    [InlineData(2026, 9, 22, 0, 0, "22nd Sep 2026 12:00am")]
    [InlineData(2026, 9, 11, 12, 0, "11th Sep 2026 12:00pm")]
    public void OrdinalDateTime_ReadsLikeUptick(int y, int m, int d, int h, int min, string expected) =>
        Assert.Equal(expected, new DateTime(y, m, d, h, min, 0).ToOrdinalDateTime());
}
