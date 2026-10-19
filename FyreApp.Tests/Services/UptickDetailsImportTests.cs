using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Clients;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Routines;
using FyreApp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Client details, routine technician and duration, asset notes and order, remark dates
public class UptickDetailsImportTests
{
    private static string CsvText(string[] headers, params Dictionary<string, string>[] rows)
    {
        static string Esc(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder(string.Join(",", headers.Select(Esc)) + "\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", headers.Select(h => Esc(r.GetValueOrDefault(h, ""))))).Append("\r\n");
        return sb.ToString();
    }

    private static Stream Csv(string[] headers, params Dictionary<string, string>[] rows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(CsvText(headers, rows)));

    [Fact]
    public async Task Clients_KeepPhonesNotesAndReportContact()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme", ExternalId = "1", UptickData = "{\"ID\":\"1\",\"Name\":\"Acme\"}" });
        await db.SaveChangesAsync();
        var text = CsvText(
            new[] { "ID", "Name", "Primary Contact BH", "Primary Contact AH", "Reference", "Primary Sector", "Notes",
                    "Billing Requirements", "Report Contact: Organisation", "Report Contact: Email To" },
            new Dictionary<string, string>
            {
                ["ID"] = "1", ["Name"] = "Acme", ["Primary Contact BH"] = "07 5577 5040", ["Primary Contact AH"] = "0422 367 583",
                ["Reference"] = "Eat Sushi", ["Primary Sector"] = "Other Services", ["Notes"] = "Call the chairperson",
                ["Billing Requirements"] = "Repeating invoice", ["Report Contact: Organisation"] = "Decant Agency",
                ["Report Contact: Email To"] = "reports@example.com"
            });
        var file = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes(text)), 0, text.Length, "file", "clients.csv");

        var result = await new ClientImportService(db).ImportAsync(file, dryRun: false);

        Assert.Equal(1, result.Updated);
        var c = await db.Clients.SingleAsync();
        Assert.Equal(("07 5577 5040", "0422 367 583", "Eat Sushi", "Other Services", "Call the chairperson", "Repeating invoice"),
            (c.PrimaryContactPhone, c.PrimaryContactAfterHours, c.Reference, c.PrimarySector, c.Notes, c.BillingRequirements));
        Assert.Equal(("Decant Agency", "reports@example.com"), (c.ReportOrganisation, c.ReportEmail));
    }

    private const string RoutineHeader =
        "ID,Routine,Due Date,Tolerance Period Start Date,Tolerance Period End Date,Status,Completed Date,Property name,Property ref,Client name,Service group,Subcontractor,Estimated Duration,Default Technician";

    private static Task<FyreApp.ViewModels.MaintenanceSchedules.ScheduleImportResultVm> ImportRoutines(AppDbContext db, string row) =>
        new ScheduleImportService(db).ImportUptickAsync(new MemoryStream(Encoding.UTF8.GetBytes(RoutineHeader + "\r\n" + row)), "routines.csv", dryRun: false);

    [Fact]
    public async Task Routines_KeepTechnicianAndDuration_AndFollowUptickWhenTheyChange()
    {
        using var db = DbContextFactory.Create();
        db.Properties.Add(new Property { Name = "Magidale", ExternalId = "P-0152", Client = new Client { Name = "Magidale Pty Ltd" } });
        await db.SaveChangesAsync();

        await ImportRoutines(db, "1,10 - Extinguishers: Annual,2026-10-31,,,P,,Magidale,P-0152,Magidale Pty Ltd,Servicing,,0:30:00,Dwayne Page");
        var o = await db.Routines.SingleAsync();
        Assert.Equal(("Dwayne Page", 30), (o.DefaultTechnician, o.EstimatedMinutes));

        // Same status, new technician in Uptick
        var second = await ImportRoutines(db, "1,10 - Extinguishers: Annual,2026-10-31,,,P,,Magidale,P-0152,Magidale Pty Ltd,Servicing,Auscoast,1:15:00,Neil");

        Assert.Equal(1, second.OccurrencesUpdated);
        Assert.Equal(("Neil", "Auscoast", 75, RoutineStatus.Pending), (o.DefaultTechnician, o.Subcontractor, o.EstimatedMinutes, o.Status));
    }

    [Theory]
    [InlineData("0:30:00", 30)]
    [InlineData("1:15:00", 75)]
    [InlineData("0:00:00", null)]
    [InlineData("", null)]
    public void ParseMinutes_ReadsUptickDurations(string input, int? expected) =>
        Assert.Equal(expected, ScheduleImportService.ParseMinutes(input));

    [Fact]
    public async Task PropertyRoutines_GroupShowsTechnicianAndEstimatedHours()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Camajay", Client = new Client { Name = "Camajay" } };
        Routine O(string due, string? tech, int? minutes) => new()
        {
            Property = site, Name = "10 - Extinguishers: Six-monthly", ServiceGroup = "Servicing",
            DueDate = DateTime.SpecifyKind(DateTime.Parse(due), DateTimeKind.Utc), DefaultTechnician = tech, EstimatedMinutes = minutes
        };
        db.Routines.AddRange(O("2026-04-30", "Neil", 30), O("2026-10-31", "Neil", 60), O("2026-06-30", null, null));
        await db.SaveChangesAsync();

        var group = Assert.Single((await new RoutineListService(db).ForPropertyAsync(site.Id, 2026)).Groups);

        Assert.Equal(("Neil", 90), (group.Technician, group.EstimatedMinutes));
    }

    [Fact]
    public async Task AssetsAndRemarks_KeepNotesOrderAndDates()
    {
        using var db = DbContextFactory.Create();
        db.Properties.Add(new Property { Name = "Acme HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } });
        await db.SaveChangesAsync();
        var sut = new UptickImportService(new UptickImporter[] { new AssetImporter(db), new RemarkImporter(db) });

        await sut.ImportAsync(Csv(new[] { "ID", "Label", "Type", "Variant", "Guid", "Property Ref", "Is Active", "Inactive Date", "Internal Note", "Inspection Order" },
                new Dictionary<string, string>
                {
                    ["ID"] = "5", ["Label"] = "Ext 1", ["Type"] = "Extinguisher", ["Property Ref"] = "P-1", ["Is Active"] = "False",
                    ["Inactive Date"] = "2026-09-30 11:50:31", ["Internal Note"] = "4.5kg to be exchanged for 2.5kg", ["Inspection Order"] = "20"
                }),
            "assets.csv", UptickExportType.Assets, dryRun: false);
        await sut.ImportAsync(Csv(new[] { "ID", "Remark Type", "Property Ref", "Severity", "Resolution", "Inactive Date", "Quoted Date" },
                new Dictionary<string, string>
                {
                    ["ID"] = "7", ["Remark Type"] = "Asset Failed", ["Property Ref"] = "P-1",
                    ["Inactive Date"] = "2026-10-13 16:05:10", ["Quoted Date"] = "2026-10-08"
                }),
            "remarks.csv", UptickExportType.Remarks, dryRun: false);

        var asset = await db.Assets.SingleAsync();
        // Queensland is UTC+10 all year
        Assert.Equal(("4.5kg to be exchanged for 2.5kg", 20, new DateTime(2026, 9, 30, 1, 50, 31, DateTimeKind.Utc)),
            (asset.InternalNote, asset.InspectionOrder, asset.InactiveDate));
        var defect = await db.Remarks.SingleAsync();
        Assert.Equal((new DateTime(2026, 10, 13, 6, 5, 10, DateTimeKind.Utc), new DateTime(2026, 10, 8)), (defect.InactiveUtc, defect.QuotedDate));
    }
}
