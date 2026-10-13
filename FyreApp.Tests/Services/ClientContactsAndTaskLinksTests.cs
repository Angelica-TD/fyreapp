using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Client contacts export, report/remark → task links, and the property's access and site info
public class ClientContactsAndTaskLinksTests
{
    private static UptickImportService CreateSut(AppDbContext db) => new(new UptickImporter[]
    {
        new PropertyImporter(db), new ClientContactImporter(db), new RemarkImporter(db), new ReportImporter(db), new TaskImporter(db)
    });

    private static Stream Csv(string[] headers, params Dictionary<string, string>[] rows)
    {
        static string Esc(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder(string.Join(",", headers.Select(Esc)) + "\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", headers.Select(h => Esc(r.GetValueOrDefault(h, ""))))).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static readonly string[] ContactHeaders =
        { "ID", "Created", "Updated", "Name", "Email", "Mobile", "Phone", "Position", "Active", "Client ID", "Client Name" };
    private static readonly string[] ReportHeaders =
        { "ID", "Ref", "Issued", "Inspected", "Report Type", "Property Ref", "Task Ref", "Task", "Task Id" };
    private static readonly string[] RemarkHeaders =
        { "ID", "Remark Type", "Property Ref", "Severity", "Resolution", "Repair Task Ref", "Repair Task ID" };
    private static readonly string[] TaskHeaders =
        { "ID", "Ref", "Name", "Scope of works", "Status", "Property Ref", "Category", "Due" };
    private static readonly string[] PropertyHeaders =
        { "ID", "Ref", "Name", "Status", "Address Street Address", "Client ID", "Client", "Inactive/Onhold Date",
          "Access Code", "Access Procedure", "Access Note", "Internal Note", "Safety Requirements", "Address Lat", "Address Lng" };

    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, string[] headers,
        UptickExportType type, params Dictionary<string, string>[] rows) =>
        CreateSut(db).ImportAsync(Csv(headers, rows), "export.csv", type, dryRun: false);

    [Fact]
    public async Task ClientContacts_ImportedUnderTheirClient_UnknownClientGetsPlaceholder()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Backpacker in Paradise", ExternalId = "285" });
        await db.SaveChangesAsync();

        var result = await Import(db, ContactHeaders, UptickExportType.ClientContacts,
            new Dictionary<string, string> { ["ID"] = "1", ["Name"] = "Courtney", ["Mobile"] = "0475161495", ["Active"] = "True", ["Client ID"] = "285", ["Client Name"] = "Backpacker in Paradise" },
            new Dictionary<string, string> { ["ID"] = "2", ["Name"] = "Mari", ["Email"] = "mari@example.com", ["Position"] = "Knight Frank Officer", ["Active"] = "True", ["Client ID"] = "3285", ["Client Name"] = "Pippin Took 2" });

        Assert.Equal(2, result.Created);
        var contacts = await db.ClientContacts.Include(c => c.Client).OrderBy(c => c.ExternalId).ToListAsync();
        Assert.Equal(("Courtney", "0475161495", "Backpacker in Paradise"), (contacts[0].Name, contacts[0].Mobile, contacts[0].Client.Name));
        Assert.Equal(("Knight Frank Officer", "Pippin Took 2", true), (contacts[1].Position, contacts[1].Client.Name, contacts[1].Client.IsPlaceholder));
    }

    [Fact]
    public async Task ClientContacts_RerunUpdatesWhatChanged()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme", ExternalId = "285" });
        await db.SaveChangesAsync();
        var row = new Dictionary<string, string> { ["ID"] = "1", ["Name"] = "Courtney", ["Active"] = "True", ["Client ID"] = "285" };

        await Import(db, ContactHeaders, UptickExportType.ClientContacts, row);
        row["Active"] = "False";
        var second = await Import(db, ContactHeaders, UptickExportType.ClientContacts, row);

        Assert.Equal((0, 1), (second.Created, second.Updated));
        Assert.False((await db.ClientContacts.AsNoTracking().SingleAsync()).Active);
    }

    [Fact]
    public async Task ReportsAndRemarks_LinkToTheirTask_WhenTheTaskIsImportedFirst()
    {
        using var db = DbContextFactory.Create();
        db.Sites.Add(new Site { Name = "Acme HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } });
        await db.SaveChangesAsync();

        await Import(db, TaskHeaders, UptickExportType.Tasks,
            new Dictionary<string, string> { ["ID"] = "46715", ["Ref"] = "T-46715", ["Name"] = "Service", ["Status"] = "COMPLETE", ["Property Ref"] = "P-1", ["Category"] = "I&T" });
        await Import(db, ReportHeaders, UptickExportType.Reports,
            new Dictionary<string, string> { ["ID"] = "9", ["Report Type"] = "Service Report", ["Property Ref"] = "P-1", ["Task Ref"] = "T-46715", ["Task Id"] = "46715" });
        await Import(db, RemarkHeaders, UptickExportType.Remarks,
            new Dictionary<string, string> { ["ID"] = "7", ["Remark Type"] = "Asset Failed", ["Property Ref"] = "P-1", ["Repair Task ID"] = "46715" });

        var task = await db.ClientTasks.SingleAsync();
        Assert.Equal(task.Id, (await db.ServiceReports.SingleAsync()).ClientTaskId);
        Assert.Equal(task.Id, (await db.Defects.SingleAsync()).RepairTaskId);
    }

    [Fact]
    public async Task ReportsAndRemarks_LinkToTheirTask_WhenTheTaskIsImportedAfter()
    {
        using var db = DbContextFactory.Create();
        db.Sites.Add(new Site { Name = "Acme HQ", ExternalId = "P-1", Client = new Client { Name = "Acme" } });
        await db.SaveChangesAsync();

        await Import(db, ReportHeaders, UptickExportType.Reports,
            new Dictionary<string, string> { ["ID"] = "9", ["Report Type"] = "Service Report", ["Property Ref"] = "P-1", ["Task Id"] = "46715" });
        await Import(db, RemarkHeaders, UptickExportType.Remarks,
            new Dictionary<string, string> { ["ID"] = "7", ["Remark Type"] = "Asset Failed", ["Property Ref"] = "P-1", ["Repair Task ID"] = "46715" });
        Assert.Null((await db.ServiceReports.AsNoTracking().SingleAsync()).ClientTaskId);

        var result = await Import(db, TaskHeaders, UptickExportType.Tasks,
            new Dictionary<string, string> { ["ID"] = "46715", ["Ref"] = "T-46715", ["Name"] = "Service", ["Status"] = "READY", ["Property Ref"] = "P-1" });

        var task = await db.ClientTasks.SingleAsync();
        Assert.Contains(result.Notes, n => n.StartsWith("1 report(s) and 1 remark(s)"));
        Assert.Equal(task.Id, (await db.ServiceReports.AsNoTracking().SingleAsync()).ClientTaskId);
        Assert.Equal(task.Id, (await db.Defects.AsNoTracking().SingleAsync()).RepairTaskId);
    }

    [Fact]
    public async Task Properties_ImportAccessAndSiteInfo()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme", ExternalId = "100" });
        await db.SaveChangesAsync();

        await Import(db, PropertyHeaders, UptickExportType.Properties, new Dictionary<string, string>
        {
            ["Ref"] = "P-1", ["Name"] = "Acme HQ", ["Status"] = "ONHOLD", ["Client ID"] = "100",
            ["Inactive/Onhold Date"] = "2026-09-11 12:58:51", ["Access Code"] = "4357#", ["Access Procedure"] = "CA",
            ["Access Note"] = "Call Pam first", ["Internal Note"] = "pls update assets", ["Safety Requirements"] = "Wear your PPE",
            ["Address Lat"] = "-28.1497592", ["Address Lng"] = "153.4754302"
        });

        var site = await db.Sites.SingleAsync();
        Assert.Equal(("4357#", "CA", "Call Pam first", "pls update assets", "Wear your PPE"),
            (site.AccessCode, site.AccessProcedure, site.AccessNote, site.InternalNote, site.SafetyRequirements));
        Assert.Equal((-28.1497592, 153.4754302), (site.Latitude!.Value, site.Longitude!.Value));
        // Queensland is UTC+10 all year
        Assert.Equal(new DateTime(2026, 9, 11, 2, 58, 51, DateTimeKind.Utc), site.InactiveDate);
    }
}
