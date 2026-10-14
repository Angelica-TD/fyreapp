using System.Globalization;
using ClosedXML.Excel;
using FyreApp.Services.Imports;
using Xunit;

namespace FyreApp.Tests.Services;

public class TabularFileReaderTests
{
    [Fact]
    public async Task ReadAsync_XlsxDateCells_AreIsoRegardlessOfCulture()
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Base Date";
            ws.Cell(1, 2).Value = "Created";
            ws.Cell(1, 3).Value = "Barcode";
            ws.Cell(2, 1).Value = new DateTime(2023, 2, 1);
            ws.Cell(2, 2).Value = new DateTime(2026, 9, 21, 14, 58, 59);
            ws.Cell(2, 3).Value = 5720;
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-AU");
        try
        {
            var rows = await TabularFileReader.ReadAsync(ms, "export.xlsx");

            var row = Assert.Single(rows);
            Assert.Equal("2023-02-01", row["Base Date"]);
            Assert.Equal("2026-09-21 14:58:59", row["Created"]);
            Assert.Equal("5720", row["Barcode"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task ReadAsync_RepeatedHeader_KeepsEveryColumn()
    {
        // Uptick's users export: licence type, then trade licence number, both headed "License"
        var csv = "\"Name\",\"License\",\"License\"\r\n\"Dwayne Page\",\"FIELD\",\"QBCC 15366855\"\r\n";

        var rows = await TabularFileReader.ReadAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), "users.csv");

        var row = Assert.Single(rows);
        Assert.Equal(("FIELD", "QBCC 15366855"), (row["License"], row["License (2)"]));
    }
}
