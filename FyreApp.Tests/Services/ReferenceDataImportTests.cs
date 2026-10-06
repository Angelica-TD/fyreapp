using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

public class ReferenceDataImportTests
{
    private static Stream Csv(string[] headers, params Dictionary<string, string>[] rows)
    {
        static string Esc(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder(string.Join(",", headers.Select(Esc)) + "\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", headers.Select(h => Esc(r.GetValueOrDefault(h, ""))))).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, UptickExportType type, Stream csv, bool dryRun = false) =>
        new UptickImportService(new UptickImporter[]
        {
            new AssetImporter(db), new RemarkImporter(db),
            new AssetTypeImporter(db), new AssetTypeVariantImporter(db), new RemarkTypeImporter(db),
            new RoutineServiceTypeImporter(db), new RoutineServiceLevelImporter(db)
        }).ImportAsync(csv, "export.csv", type, dryRun);

    private static readonly string[] AssetTypeHeaders = { "ID", "Name", "Description", "Active", "Category", "Inspection Criteria", "Sequence Group", "Sequence Pattern", "Applicable Routines" };
    private static readonly string[] VariantHeaders = { "ID", "Asset Type", "Asset Type ID", "Default Replacement Product", "Name", "Active" };
    private static readonly string[] RemarkTypeHeaders = { "ID", "Active", "Asset Type", "Asset Type Tag", "Label", "Description", "Severity Display", "Resolution", "Owner Responsible" };
    private static readonly string[] ServiceTypeHeaders = { "ID", "Name", "Active", "Standard", "Standard Reference", "Default Performance Standard", "Service Group", "Custom" };
    private static readonly string[] LevelHeaders = { "ID", "Name", "Display Code", "Interval", "Frequency", "Tolerance Interval", "Tolerance Unit", "Active?", "Routine Service Type ID", "Routine Service Type Name" };

    private static Dictionary<string, string> ExtinguisherType => new()
    {
        ["ID"] = "10", ["Name"] = "Fire Extinguisher", ["Active"] = "True",
        ["Category"] = "AS1851 Sec 10 - Portable Fire Equipment", ["Inspection Criteria"] = "AS1851-2012, Section 10",
        ["Sequence Group"] = "Portable Equipment", ["Sequence Pattern"] = "{ref} - {variant}", ["Applicable Routines"] = "10 - Portable and Wheeled Fire Extinguishers"
    };

    [Fact]
    public async Task AssetTypes_FillInTypeCreatedByAssetsImport_AndUpdateOnRerun()
    {
        using var db = DbContextFactory.Create();
        db.AssetTypes.Add(new AssetType { Name = "Fire Extinguisher" }); // as the assets import creates it
        await db.SaveChangesAsync();

        var first = await Import(db, UptickExportType.AssetTypes, Csv(AssetTypeHeaders, ExtinguisherType));
        var changed = ExtinguisherType;
        changed["Sequence Group"] = "Portables";
        var second = await Import(db, UptickExportType.AssetTypes, Csv(AssetTypeHeaders, changed));

        Assert.Equal((0, 0), (first.Created, second.Created));
        Assert.Contains(second.Notes, n => n.Contains("1 existing asset types were updated"));
        var type = await db.AssetTypes.SingleAsync();
        Assert.Equal(("10", "AS1851-2012, Section 10", "Portables"), (type.ExternalId, type.InspectionCriteria, type.SequenceGroup));
        Assert.Contains("\"Applicable Routines\"", type.UptickData);
    }

    [Fact]
    public async Task Variants_CreateUnderTypeAndLinkExistingAssets()
    {
        using var db = DbContextFactory.Create();
        var type = new AssetType { Name = "Fire Extinguisher", ExternalId = "10" };
        var site = new Site { Name = "HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } };
        db.Assets.AddRange(
            new Asset { Name = "Ext 1", Site = site, Variant = "DCP AB(E) 4.5KG", AssetTypes = { type } },
            new Asset { Name = "Ext 2", Site = site, Variant = "Something else", AssetTypes = { type } });
        await db.SaveChangesAsync();

        var result = await Import(db, UptickExportType.AssetTypeVariants, Csv(VariantHeaders, new Dictionary<string, string>
        {
            ["ID"] = "7", ["Asset Type"] = "Fire Extinguisher", ["Asset Type ID"] = "10", ["Name"] = "DCP AB(E) 4.5KG",
            ["Default Replacement Product"] = "4.5Kg ABE Extinguisher (Exchange)", ["Active"] = "True"
        }));

        Assert.Equal(1, result.Created);
        var variant = await db.AssetTypeVariants.SingleAsync();
        Assert.Equal((type.Id, "4.5Kg ABE Extinguisher (Exchange)"), (variant.AssetTypeId, variant.DefaultReplacementProduct));
        var assets = await db.Assets.OrderBy(a => a.Name).ToListAsync();
        Assert.Equal(new int?[] { variant.Id, null }, assets.Select(a => a.AssetTypeVariantId));
    }

    [Fact]
    public async Task RemarkTypes_LinkExistingDefectsByUptickId_AndNewRemarksLinkOnImport()
    {
        using var db = DbContextFactory.Create();
        var site = new Site { Name = "HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } };
        db.Defects.Add(new Defect { Site = site, ExternalId = "500", RemarkType = "1.02 - Battery missing", UptickData = "{\"ID\":\"500\",\"Remark Type ID\":\"723\"}" });
        await db.SaveChangesAsync();

        var result = await Import(db, UptickExportType.RemarkTypes, Csv(RemarkTypeHeaders, new Dictionary<string, string>
        {
            ["ID"] = "723", ["Active"] = "True", ["Asset Type"] = "Smoke Alarm", ["Asset Type Tag"] = "Electrical Systems",
            ["Label"] = "1.02 - Battery missing indication", ["Severity Display"] = "Critical defect",
            ["Resolution"] = "Supply and install new batteries", ["Owner Responsible"] = "False"
        }));

        Assert.Equal(1, result.Created);
        var remarkType = await db.RemarkTypes.Include(r => r.AssetType).SingleAsync();
        Assert.Equal(("Critical defect", "Smoke Alarm"), (remarkType.SeverityLabel, remarkType.AssetType!.Name));
        Assert.Equal(remarkType.Id, (await db.Defects.SingleAsync()).RemarkTypeId);

        // A remark imported afterwards links straight away
        await Import(db, UptickExportType.Remarks, Csv(
            new[] { "ID", "Remark Type", "Remark Type ID", "Property Ref", "Severity", "Resolution" },
            new Dictionary<string, string> { ["ID"] = "501", ["Remark Type"] = "1.02 - Battery missing indication", ["Remark Type ID"] = "723", ["Property Ref"] = "P-1" }));
        Assert.Equal(remarkType.Id, (await db.Defects.SingleAsync(d => d.ExternalId == "501")).RemarkTypeId);
    }

    [Fact]
    public async Task Levels_StoreIntervalUnderTheirServiceType()
    {
        using var db = DbContextFactory.Create();
        await Import(db, UptickExportType.RoutineServiceTypes, Csv(ServiceTypeHeaders, new Dictionary<string, string>
        {
            ["ID"] = "16", ["Name"] = "10 - Portable and Wheeled Fire Extinguishers", ["Active"] = "True",
            ["Standard Reference"] = "Section 10", ["Service Group"] = "Servicing - Portables & Fire Equipment"
        }));

        var result = await Import(db, UptickExportType.RoutineServiceLevels, Csv(LevelHeaders, new Dictionary<string, string>
        {
            ["ID"] = "48", ["Name"] = "Six-monthly", ["Display Code"] = "H", ["Interval"] = "6", ["Frequency"] = "M",
            ["Tolerance Interval"] = "1", ["Tolerance Unit"] = "M", ["Active?"] = "True",
            ["Routine Service Type ID"] = "16", ["Routine Service Type Name"] = "10 - Portable and Wheeled Fire Extinguishers"
        }));

        Assert.Equal(1, result.Created);
        var level = await db.RoutineServiceLevels.Include(l => l.RoutineServiceType).SingleAsync();
        Assert.Equal((6, 1, "M", "Section 10"), (level.IntervalMonths, level.ToleranceInterval, level.ToleranceUnit, level.RoutineServiceType.StandardReference));
        Assert.Single(await db.RoutineServiceTypes.ToListAsync());
    }

    [Theory]
    [InlineData(6, "M", 6)]
    [InlineData(2, "Y", 24)]
    [InlineData(14, "D", null)]
    [InlineData(0, "M", null)]
    public void IntervalMonths_ConvertsUnits(int interval, string unit, int? expected)
    {
        Assert.Equal(expected, RoutineServiceLevelImporter.IntervalMonths(interval, unit));
    }

    // -------------------------------------------------------
    // Routines import with levels
    // -------------------------------------------------------

    private const string RoutineHeader =
        "ID,Routine,Due Date,Status,Completed Date,Property name,Property ref,Client name";

    private static Stream Routines(params (string Routine, string Due)[] rows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(RoutineHeader + "\r\n" +
            string.Join("\r\n", rows.Select((r, i) => $"{i + 1},{r.Routine},{r.Due},P,,HQ,P-1,Acme"))));

    private static async Task SeedLevelsAsync(AppDbContext db)
    {
        var drill = new RoutineServiceType { Name = "Annual Evacuation Drill", ExternalId = "31" };
        var custom = new RoutineServiceType { Name = "Fire Wardens", ExternalId = "40" };
        var ext = new RoutineServiceType { Name = "10 - Extinguishers", ExternalId = "16" };
        db.RoutineServiceLevels.AddRange(
            new RoutineServiceLevel { Name = "Annual Evacuation Drill", IntervalMonths = 1, RoutineServiceType = drill, ExternalId = "76" },
            new RoutineServiceLevel { Name = "Warden Training", IntervalMonths = 24, RoutineServiceType = custom, ExternalId = "77" },
            new RoutineServiceLevel { Name = "Six-monthly", IntervalMonths = 6, RoutineServiceType = ext, ExternalId = "48" });
        db.Sites.Add(new Site { Name = "HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Routines_UseLevelIntervalWhenNameHasNoFrequency_ButNameWinsWhenItHasOne()
    {
        using var db = DbContextFactory.Create();
        await SeedLevelsAsync(db);

        var result = await new ScheduleImportService(db).ImportUptickAsync(Routines(
            ("Fire Wardens: Warden Training", "2027-01-01"),                       // no frequency in name -> level (24)
            ("Annual Evacuation Drill: Annual Evacuation Drill", "2027-02-01"),    // name says annual -> 12, not the level's 1
            ("10 - Extinguishers: Six-monthly", "2027-03-01")), "routines.csv", dryRun: false);

        Assert.Equal(0, result.SkippedUnsupportedFrequency);
        var schedules = await db.MaintenanceSchedules.Include(s => s.MaintenanceInterval).Include(s => s.RoutineLevels).ToListAsync();
        Assert.Equal(new[] { 6, 12, 24 }, schedules.Select(s => s.MaintenanceInterval.Months).OrderBy(m => m));
        Assert.All(schedules, s => Assert.Single(s.RoutineLevels));
        Assert.Equal("76", schedules.Single(s => s.MaintenanceInterval.Months == 12).RoutineLevels.Single().ExternalId);
    }

    [Fact]
    public async Task Routines_Rerun_LinksExistingScheduleToRoutinesItCovers()
    {
        using var db = DbContextFactory.Create();
        var site = new Site { Name = "HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } };
        db.MaintenanceSchedules.Add(new MaintenanceSchedule
        {
            TargetType = ScheduleTargetType.Site, Site = site, IsActive = true,
            MaintenanceInterval = new MaintenanceInterval { Name = "Six-monthly", Months = 6 },
            StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), NextRunDate = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.RoutineServiceLevels.Add(new RoutineServiceLevel
        {
            Name = "Six-monthly", IntervalMonths = 6, ExternalId = "48",
            RoutineServiceType = new RoutineServiceType { Name = "10 - Extinguishers", ExternalId = "16" }
        });
        await db.SaveChangesAsync();

        var result = await new ScheduleImportService(db).ImportUptickAsync(
            Routines(("10 - Extinguishers: Six-monthly", "2027-03-01")), "routines.csv", dryRun: false);

        Assert.Equal((0, 1, 1), (result.Created, result.SkippedExisting, result.RoutineLinksAdded));
        Assert.Single((await db.MaintenanceSchedules.Include(s => s.RoutineLevels).SingleAsync()).RoutineLevels);
    }
}
