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
            Assert.Equal(1, result.SkippedDuplicateName);
            Assert.Equal(0, result.Failed);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
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
