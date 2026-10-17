using System.Text;
using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Assets;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Services.Timeline;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Assets;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Property and asset Timelines, as Uptick keeps them: "Access note was changed to ...", by whom and when
public class TimelineTests
{
    private static Stream Csv(string[] headers, params Dictionary<string, string>[] rows)
    {
        static string Esc(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder(string.Join(",", headers.Select(Esc)) + "\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", headers.Select(h => Esc(r.GetValueOrDefault(h, ""))))).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, UptickExportType type, Stream csv) =>
        new UptickImportService(new UptickImporter[] { new PropertyImporter(db), new AssetImporter(db) })
            .ImportAsync(csv, "export.csv", type, dryRun: false);

    private static readonly string[] PropertyHeaders =
        { "ID", "Ref", "Name", "Created", "Updated", "Status", "Access Note", "Address", "Address Street Address", "Client ID", "Client" };
    private static readonly string[] AssetHeaders =
        { "ID", "Created", "Updated", "Is Active", "Ref", "Label", "Location", "Compliant", "Last Service Date", "Inspection Ref", "Quantity", "Guid", "Type", "Variant", "Property Ref", "Property" };

    private static Dictionary<string, string> GoldCoastConcrete => new()
    {
        ["ID"] = "14484", ["Ref"] = "P-3340", ["Name"] = "Gold Coast Concrete", ["Created"] = "2026-10-13 14:21:00",
        ["Updated"] = "2026-10-13 14:21:00", ["Status"] = "ACTIVE", ["Client ID"] = "500", ["Client"] = "Jane Clandy",
        ["Address"] = "6/11 Coghill Dr, Currumbin Waters QLD 4223"
    };

    [Fact]
    public async Task PropertyReimport_UpdatesTheProperty_AndLeavesTheTimelineToUptick()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Jane Clandy", ExternalId = "500" });
        await db.SaveChangesAsync();

        await Import(db, UptickExportType.Properties, Csv(PropertyHeaders, GoldCoastConcrete));
        var changed = GoldCoastConcrete;
        changed["Updated"] = "2026-10-13 14:26:00";
        changed["Access Note"] = "Please send reminder email";
        await Import(db, UptickExportType.Properties, Csv(PropertyHeaders, changed));

        // The change is on Uptick's own timeline, which the migration brings in; the import doesn't make one up
        Assert.Equal("Please send reminder email", (await db.Properties.SingleAsync()).AccessNote);
        Assert.Empty(db.TimelineEvents);
        Assert.Empty(await new TimelineService(db).ForPropertyAsync(await db.Properties.SingleAsync(), notesOnly: false));
    }

    [Fact]
    public async Task AssetReimport_UpdatesTheAsset_AndLeavesTheTimelineToUptick()
    {
        using var db = DbContextFactory.Create();
        db.Properties.Add(new Property { Name = "Kamber", ExternalId = "P-3330", Client = new Client { Name = "Kamber" } });
        await db.SaveChangesAsync();
        var row = new Dictionary<string, string>
        {
            ["ID"] = "41675", ["Created"] = "2026-09-07 13:07:00", ["Updated"] = "2026-09-07 13:07:00", ["Is Active"] = "True",
            ["Ref"] = "001", ["Label"] = "001 - DCP AB(E) 4.5KG Portable Fire Extinguisher", ["Location"] = "Right side of roller door",
            ["Compliant"] = "N/A", ["Inspection Ref"] = "TAG-9", ["Quantity"] = "1", ["Type"] = "Fire Extinguisher",
            ["Variant"] = "DCP AB(E) 4.5KG", ["Property Ref"] = "P-3330", ["Property"] = "Kamber"
        };
        await Import(db, UptickExportType.Assets, Csv(AssetHeaders, row));
        var asset = await db.Assets.SingleAsync();
        Assert.Equal(("TAG-9", "1"), (asset.Serial, asset.Quantity));

        row["Updated"] = "2026-09-07 13:26:00";
        row["Compliant"] = "Pass";
        row["Last Service Date"] = "2026-09-07";
        await Import(db, UptickExportType.Assets, Csv(AssetHeaders, row));

        Assert.Equal("Pass", (await db.Assets.SingleAsync()).Compliance);
        Assert.Empty(await new TimelineService(db).ForAssetAsync(await db.Assets.SingleAsync(), notesOnly: false));
    }

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
