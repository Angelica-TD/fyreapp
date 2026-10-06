using System.Globalization;
using ClosedXML.Excel;
using FyreApp.Models;
using FyreApp.Services.Clients;
using FyreApp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FyreApp.Tests.Services;

public class ClientImportServiceTests
{
    private static IFormFile Xlsx(Action<IXLWorksheet> fill)
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            fill(wb.AddWorksheet("Sheet1"));
            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", "clients.xlsx");
    }

    [Fact]
    public async Task ImportAsync_DryRun_ReadsXlsxWithSharedReader()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Existing", ExternalId = "1" });
        await db.SaveChangesAsync();

        var file = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Name";
            ws.Cell(1, 3).Value = "Created";
            ws.Cell(2, 1).Value = 13827;
            ws.Cell(2, 2).Value = "RMC Service";
            ws.Cell(2, 3).Value = new DateTime(2026, 9, 25, 11, 55, 36);
            // blank row is ignored
            ws.Cell(4, 1).Value = 1;
            ws.Cell(4, 2).Value = "Existing";
        });

        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-AU");
        try
        {
            var result = await new ClientImportService(db).ImportAsync(file, dryRun: true);

            Assert.Equal(2, result.TotalRows);
            Assert.Equal(1, result.Created);
            Assert.Equal(1, result.SkippedDuplicateExternalId);
            Assert.Equal(0, result.Failed);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static IFormFile Csv(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "clients.csv");
    }

    [Fact]
    public async Task ImportAsync_DuplicateNames_ImportedAsIs()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme", ExternalId = "1" });
        await db.SaveChangesAsync();

        // Uptick allows clients to share a name: same as an existing client, and twice in the file
        var result = await new ClientImportService(db).ImportAsync(
            Csv("ID,Name\r\n2,Acme\r\n3,Acme\r\n4,Other\r\n"), dryRun: true);

        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.SkippedDuplicateExternalId);
    }

    [Fact]
    public async Task ImportAsync_RerunSameId_SkippedAsExisting()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme", ExternalId = "1" });
        await db.SaveChangesAsync();

        var result = await new ClientImportService(db).ImportAsync(
            Csv("ID,Name\r\n1,Acme\r\n"), dryRun: true);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedDuplicateExternalId);
    }

    [Fact]
    public async Task ImportAsync_PlaceholderClient_IsFilledInNotSkipped()
    {
        using var db = DbContextFactory.Create();
        db.Clients.Add(new Client { Name = "Acme", ExternalId = "1", IsPlaceholder = true, Active = false });
        await db.SaveChangesAsync();

        var result = await new ClientImportService(db).ImportAsync(
            Csv("ID,Name\r\n1,Acme Pty Ltd\r\n"), dryRun: true);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.FilledPlaceholders);
        Assert.Equal(0, result.SkippedDuplicateExternalId);
    }

    [Fact]
    public async Task ImportAsync_MissingName_ImportedNotSkipped()
    {
        using var db = DbContextFactory.Create();

        var result = await new ClientImportService(db).ImportAsync(
            Csv("ID,Name\r\n1,\r\n"), dryRun: true);

        Assert.Equal(1, result.Created);
    }

    [Fact]
    public async Task ImportAsync_LongMobileWithSeveralNumbers_IsImported()
    {
        using var db = DbContextFactory.Create();
        var mobile = "0412 345 678 / 0498 765 432 (after hours)"; // 41 chars, over the old 32 limit

        var result = await new ClientImportService(db).ImportAsync(
            Csv($"ID,Name,Primary Contact Mobile\r\n1,Acme,{mobile}\r\n"), dryRun: true);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.SkippedInvalid);
    }

    [Fact]
    public async Task ImportAsync_UnsupportedExtension_Fails()
    {
        using var db = DbContextFactory.Create();
        var file = new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "file", "clients.pdf");

        var result = await new ClientImportService(db).ImportAsync(file, dryRun: true);

        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Messages, m => m.Contains(".pdf"));
    }
}
