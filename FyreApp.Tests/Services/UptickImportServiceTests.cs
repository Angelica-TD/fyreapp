using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.DataReset;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FyreApp.Tests.Services;

public class UptickImportServiceTests
{
    private static UptickImportService CreateSut(AppDbContext db) => new(new UptickImporter[]
    {
        new PropertyImporter(db),
        new PropertyContactImporter(db),
        new AssetImporter(db),
        new RemarkImporter(db),
        new ReportImporter(db)
    });

    // Builds a CSV with the given headers; each row maps header -> value (missing headers are blank).
    private static Stream Csv(string[] headers, params Dictionary<string, string>[] rows)
    {
        static string Esc(string v) => v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;

        var sb = new StringBuilder(string.Join(",", headers.Select(Esc)) + "\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", headers.Select(h => Esc(r.GetValueOrDefault(h, ""))))).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static readonly string[] PropertyHeaders =
        { "ID", "Ref", "Name", "Status", "Address", "Address Street Address", "Address City", "Address State", "Address Postcode", "Client ID", "Client" };
    private static readonly string[] ContactHeaders =
        { "ID", "Property Ref", "Property Name", "Organisation", "Contact Name", "Email", "Mobile", "Invoice req", "Report req", "Quote req", "Notification req", "Role", "Active" };
    private static readonly string[] AssetHeaders =
        { "ID", "Is Active", "Ref", "Label", "Location", "Compliant", "Barcode", "Base Date", "Last Service Date", "Guid", "Type", "Variant", "Property Ref", "Property" };
    private static readonly string[] RemarkHeaders =
        { "ID", "Created", "Active", "Status", "Remark Type", "Asset ID", "Property Name", "Property Ref", "Created on Task Ref", "Description", "Severity", "Severity Display", "Resolution" };
    private static readonly string[] ReportHeaders =
        { "ID", "Ref", "Issued", "Inspected", "Report Type", "Compliant", "Property Ref", "Property", "Task Ref", "Task", "Technician", "Published" };

    private static async Task<Client> SeedClientAsync(AppDbContext db, string name = "Acme", string? ext = "100")
    {
        var c = new Client { Name = name, ExternalId = ext };
        db.Clients.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Site> SeedSiteAsync(AppDbContext db, string propertyRef = "P-1")
    {
        var s = new Site { Name = "Acme HQ", ExternalId = propertyRef, Client = new Client { Name = "Acme " + propertyRef } };
        db.Sites.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    // -------------------------------------------------------
    // Detection
    // -------------------------------------------------------

    [Fact]
    public async Task Import_AutoDetectsExportType()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db);

        var result = await CreateSut(db).ImportAsync(
            Csv(ReportHeaders, new Dictionary<string, string> { ["ID"] = "1", ["Property Ref"] = "P-1", ["Issued"] = "2026-09-21" }),
            "reports.csv", type: null, dryRun: true);

        Assert.Null(result.Error);
        Assert.Equal(UptickExportType.Reports, result.Type);
        Assert.Equal(1, result.Created);
    }

    [Fact]
    public async Task Import_WrongExportForSelectedType_ReturnsError()
    {
        using var db = DbContextFactory.Create();

        var result = await CreateSut(db).ImportAsync(
            Csv(ReportHeaders, new Dictionary<string, string> { ["ID"] = "1" }),
            "reports.csv", UptickExportType.Assets, dryRun: true);

        Assert.NotNull(result.Error);
        Assert.Contains("Assets", result.Error);
    }

    [Fact]
    public async Task Import_ClientsExport_PointsToClientImport()
    {
        using var db = DbContextFactory.Create();

        var result = await CreateSut(db).ImportAsync(
            Csv(new[] { "ID", "Name", "Property Count (Total)", "Primary Contact Name" }, new Dictionary<string, string> { ["ID"] = "1", ["Name"] = "Acme" }),
            "clients.csv", type: null, dryRun: true);

        Assert.Contains("Clients", result.Error);
    }

    // -------------------------------------------------------
    // Properties
    // -------------------------------------------------------

    [Fact]
    public async Task Properties_CreatesSiteMatchedOnClientExternalId()
    {
        using var db = DbContextFactory.Create();
        var client = await SeedClientAsync(db, ext: "13794");

        var result = await CreateSut(db).ImportAsync(Csv(PropertyHeaders, new Dictionary<string, string>
            {
                ["ID"] = "14352", ["Ref"] = "P-3335", ["Name"] = "Otautahi", ["Status"] = "ACTIVE",
                ["Address"] = "6 Nuban St, Currumbin Waters QLD 4223, Australia", ["Address Street Address"] = "6 Nuban St",
                ["Address City"] = "Currumbin Waters", ["Address State"] = "QLD", ["Address Postcode"] = "4223",
                ["Client ID"] = "13794", ["Client"] = "Some other name"
            }),
            "properties.csv", UptickExportType.Properties, dryRun: false);

        Assert.Equal(1, result.Created);
        var site = await db.Sites.SingleAsync();
        Assert.Equal(client.Id, site.ClientId);
        Assert.Equal("P-3335", site.ExternalId);
        Assert.Equal("6 Nuban St, Currumbin Waters QLD 4223", site.AddressDisplay);
        Assert.Equal("Currumbin Waters", site.Suburb);
        Assert.True(site.Active);
    }

    [Fact]
    public async Task Properties_InactiveStatus_ImportsInactive()
    {
        using var db = DbContextFactory.Create();
        await SeedClientAsync(db);

        await CreateSut(db).ImportAsync(Csv(PropertyHeaders,
                new Dictionary<string, string> { ["Ref"] = "P-1", ["Name"] = "Old site", ["Status"] = "INACTIVE", ["Client ID"] = "100" }),
            "p.csv", UptickExportType.Properties, dryRun: false);

        Assert.False((await db.Sites.SingleAsync()).Active);
    }

    [Fact]
    public async Task Properties_LinksExistingSiteByClientAndName()
    {
        using var db = DbContextFactory.Create();
        var client = await SeedClientAsync(db);
        db.Sites.Add(new Site { Name = "Acme HQ", ClientId = client.Id });
        await db.SaveChangesAsync();

        var result = await CreateSut(db).ImportAsync(Csv(PropertyHeaders,
                new Dictionary<string, string> { ["Ref"] = "P-9", ["Name"] = "acme hq", ["Client ID"] = "100" }),
            "p.csv", UptickExportType.Properties, dryRun: false);

        Assert.Equal(0, result.Created);
        Assert.Single(result.Notes);
        Assert.Equal("P-9", (await db.Sites.SingleAsync()).ExternalId);
    }

    [Fact]
    public async Task Properties_UnknownClient_Skipped()
    {
        using var db = DbContextFactory.Create();

        var result = await CreateSut(db).ImportAsync(Csv(PropertyHeaders,
                new Dictionary<string, string> { ["Ref"] = "P-1", ["Name"] = "X", ["Client ID"] = "404", ["Client"] = "Nobody" }),
            "p.csv", UptickExportType.Properties, dryRun: false);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped["Client not found"]);
        Assert.Empty(await db.Sites.ToListAsync());
    }

    [Fact]
    public async Task Properties_RerunSkipsAlreadyImported()
    {
        using var db = DbContextFactory.Create();
        await SeedClientAsync(db);
        var row = new Dictionary<string, string> { ["Ref"] = "P-1", ["Name"] = "X", ["Client ID"] = "100" };

        await CreateSut(db).ImportAsync(Csv(PropertyHeaders, row), "p.csv", UptickExportType.Properties, dryRun: false);
        var second = await CreateSut(db).ImportAsync(Csv(PropertyHeaders, row), "p.csv", UptickExportType.Properties, dryRun: false);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.SkippedExisting);
        Assert.Equal(1, await db.Sites.CountAsync());
    }

    // -------------------------------------------------------
    // Contacts
    // -------------------------------------------------------

    [Fact]
    public async Task Contacts_CreatesContactWithFlags()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, "TRAINING");

        var result = await CreateSut(db).ImportAsync(Csv(ContactHeaders, new Dictionary<string, string>
            {
                ["ID"] = "1", ["Property Ref"] = "TRAINING", ["Contact Name"] = "Jose", ["Email"] = "jose@example.com",
                ["Mobile"] = "234234234", ["Report req"] = "TRUE", ["Invoice req"] = "FALSE", ["Role"] = "propertymanager", ["Active"] = "TRUE"
            }),
            "contacts.csv", UptickExportType.PropertyContacts, dryRun: false);

        Assert.Equal(1, result.Created);
        var contact = await db.SiteContacts.SingleAsync();
        Assert.Equal(site.Id, contact.SiteId);
        Assert.Equal("Jose", contact.Name);
        Assert.True(contact.ReportRequired);
        Assert.False(contact.InvoiceRequired);
        Assert.Equal("propertymanager", contact.Role);
    }

    [Fact]
    public async Task Contacts_UnknownProperty_Skipped()
    {
        using var db = DbContextFactory.Create();

        var result = await CreateSut(db).ImportAsync(Csv(ContactHeaders,
                new Dictionary<string, string> { ["ID"] = "1", ["Property Ref"] = "P-404", ["Contact Name"] = "Sue" }),
            "contacts.csv", UptickExportType.PropertyContacts, dryRun: false);

        Assert.Equal(1, result.Skipped["Property not found"]);
    }

    // -------------------------------------------------------
    // Assets
    // -------------------------------------------------------

    [Fact]
    public async Task Assets_CreatesAssetAndType()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, "P-0134");

        var result = await CreateSut(db).ImportAsync(Csv(AssetHeaders, new Dictionary<string, string>
            {
                ["ID"] = "42237", ["Is Active"] = "TRUE", ["Ref"] = "4", ["Label"] = "004 - DCP AB(E) 2.5KG Portable Fire Extinguisher",
                ["Location"] = "top of stair", ["Compliant"] = "Pass", ["Barcode"] = "5720", ["Base Date"] = "2023-02-01",
                ["Last Service Date"] = "2026-09-21", ["Guid"] = "565115c4", ["Type"] = "Fire Extinguisher",
                ["Variant"] = "DCP AB(E) 2.5KG", ["Property Ref"] = "P-0134"
            }),
            "assets.csv", UptickExportType.Assets, dryRun: false);

        Assert.Equal(1, result.Created);
        Assert.Contains(result.Notes, n => n.Contains("Fire Extinguisher"));

        var asset = await db.Assets.Include(a => a.AssetTypes).SingleAsync();
        Assert.Equal(site.Id, asset.SiteId);
        Assert.Equal("42237", asset.ExternalId);
        Assert.Equal("top of stair", asset.Location);
        Assert.Equal(new DateTime(2026, 9, 21), asset.LastServiceDate);
        Assert.Equal(DateTimeKind.Utc, asset.LastServiceDate!.Value.Kind);
        Assert.Equal("Fire Extinguisher", Assert.Single(asset.AssetTypes).Name);
    }

    [Fact]
    public async Task Assets_ReusesExistingTypeCaseInsensitively()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db);
        db.AssetTypes.Add(new AssetType { Name = "fire extinguisher" });
        await db.SaveChangesAsync();

        var result = await CreateSut(db).ImportAsync(Csv(AssetHeaders,
                new Dictionary<string, string> { ["ID"] = "1", ["Label"] = "A", ["Type"] = "Fire Extinguisher", ["Property Ref"] = "P-1" },
                new Dictionary<string, string> { ["ID"] = "2", ["Label"] = "B", ["Type"] = "Fire Extinguisher", ["Property Ref"] = "P-1" }),
            "assets.csv", UptickExportType.Assets, dryRun: false);

        Assert.Empty(result.Notes);
        Assert.Equal(1, await db.AssetTypes.CountAsync());
    }

    [Fact]
    public async Task Assets_DuplicateIdInFile_SkipsLaterRow()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db);

        var result = await CreateSut(db).ImportAsync(Csv(AssetHeaders,
                new Dictionary<string, string> { ["ID"] = "1", ["Label"] = "A", ["Property Ref"] = "P-1" },
                new Dictionary<string, string> { ["ID"] = "1", ["Label"] = "A again", ["Property Ref"] = "P-1" }),
            "assets.csv", UptickExportType.Assets, dryRun: false);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped["Duplicate in file"]);
    }

    // -------------------------------------------------------
    // Remarks
    // -------------------------------------------------------

    [Fact]
    public async Task Remarks_LinksToAssetAndConvertsCreatedToUtc()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, "P-0905");
        var asset = new Asset { Name = "Extinguisher", SiteId = site.Id, ExternalId = "16980" };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var result = await CreateSut(db).ImportAsync(Csv(RemarkHeaders, new Dictionary<string, string>
            {
                ["ID"] = "53189", ["Created"] = "2026-09-21 14:58:59", ["Active"] = "TRUE", ["Status"] = "Needs Quoting",
                ["Remark Type"] = "00 - Asset Failed", ["Asset ID"] = "16980", ["Property Ref"] = "P-0905",
                ["Description"] = "Asset failed expired", ["Severity"] = "2", ["Severity Display"] = "Non-conformance",
                ["Resolution"] = "Exchanged on 21.09.26", ["Created on Task Ref"] = "T-45073"
            }),
            "remarks.csv", UptickExportType.Remarks, dryRun: false);

        Assert.Equal(1, result.Created);
        var defect = await db.Defects.SingleAsync();
        Assert.Equal(asset.Id, defect.AssetId);
        Assert.Equal(2, defect.Severity);
        Assert.Equal("T-45073", defect.RaisedOnTaskRef);
        // Queensland is UTC+10 all year
        Assert.Equal(new DateTime(2026, 9, 21, 4, 58, 59, DateTimeKind.Utc), defect.RaisedUtc);
    }

    [Fact]
    public async Task Remarks_UnknownAsset_AttachedToPropertyOnly()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db);

        var result = await CreateSut(db).ImportAsync(Csv(RemarkHeaders,
                new Dictionary<string, string> { ["ID"] = "1", ["Remark Type"] = "Asset Failed", ["Asset ID"] = "999", ["Property Ref"] = "P-1" }),
            "remarks.csv", UptickExportType.Remarks, dryRun: false);

        Assert.Equal(1, result.Created);
        Assert.Single(result.Notes);
        Assert.Null((await db.Defects.SingleAsync()).AssetId);
    }

    // -------------------------------------------------------
    // Reports
    // -------------------------------------------------------

    [Fact]
    public async Task Reports_CreatesReport()
    {
        using var db = DbContextFactory.Create();
        var site = await SeedSiteAsync(db, "P-1972");

        var result = await CreateSut(db).ImportAsync(Csv(ReportHeaders, new Dictionary<string, string>
            {
                ["ID"] = "51081", ["Ref"] = "R-51081", ["Issued"] = "2026-09-21", ["Inspected"] = "2026-09-21",
                ["Report Type"] = "Service Report", ["Compliant"] = "TRUE", ["Property Ref"] = "P-1972",
                ["Task Ref"] = "T-44925", ["Technician"] = "Trent Turner", ["Published"] = "TRUE"
            }),
            "reports.csv", UptickExportType.Reports, dryRun: false);

        Assert.Equal(1, result.Created);
        var report = await db.ServiceReports.SingleAsync();
        Assert.Equal(site.Id, report.SiteId);
        Assert.Equal("R-51081", report.Ref);
        Assert.True(report.Compliant);
        Assert.True(report.Published);
        Assert.Equal(new DateTime(2026, 9, 21), report.IssuedDate);
    }

    [Fact]
    public async Task DryRun_SavesNothing()
    {
        using var db = DbContextFactory.Create();
        await SeedSiteAsync(db);

        var result = await CreateSut(db).ImportAsync(Csv(ReportHeaders,
                new Dictionary<string, string> { ["ID"] = "1", ["Property Ref"] = "P-1", ["Issued"] = "2026-09-21" }),
            "reports.csv", UptickExportType.Reports, dryRun: true);

        Assert.Equal(1, result.Created);
        Assert.Empty(await db.ServiceReports.ToListAsync());
    }

    // -------------------------------------------------------
    // Data reset toggle
    // -------------------------------------------------------

    [Fact]
    public async Task DataReset_DisabledByDefault_AndToggles()
    {
        using var db = DbContextFactory.Create();
        var sut = new DataResetService(db, NullLogger<DataResetService>.Instance);

        Assert.False(await sut.IsEnabledAsync());

        await sut.SetEnabledAsync(true, "dev@example.com");
        Assert.True(await sut.IsEnabledAsync());

        await sut.SetEnabledAsync(false, "dev@example.com");
        Assert.False(await sut.IsEnabledAsync());
    }

    [Fact]
    public async Task DataReset_WhenDisabled_Throws()
    {
        using var db = DbContextFactory.Create();
        var sut = new DataResetService(db, NullLogger<DataResetService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ResetAsync("dev@example.com"));
    }
}
