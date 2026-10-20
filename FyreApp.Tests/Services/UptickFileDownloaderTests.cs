using System.Net;
using System.Security.Cryptography;
using System.Text;
using FyreApp.Models;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Api;
using FyreApp.Services.Uptick.Files;
using FyreApp.Services.Uptick.Migration;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FyreApp.Tests.Services;

// Copying Uptick's files: report PDFs and document files, checked against Uptick's size and MD5, skipped once held
public class UptickFileDownloaderTests
{
    private sealed class MemoryStore : IFileStore
    {
        public Dictionary<string, (byte[] Bytes, string? Type)> Files { get; } = new();
        public bool IsConfigured { get; set; } = true;
        public string Description => "Memory";

        public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Files[key] = (copy.ToArray(), contentType);
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var f) ? new MemoryStream(f.Bytes) : null);
    }

    // Uptick's storage: what each signed link serves
    private sealed class Storage : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> Fetched { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Fetched.Add(path);
            return Task.FromResult(Files.TryGetValue(path, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }

    private static readonly byte[] Photo = Encoding.UTF8.GetBytes("a remark photo");
    private static readonly byte[] Signature = Encoding.UTF8.GetBytes("a signature");
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF report");

    private static string Md5(byte[] b) => Convert.ToHexStringLower(MD5.HashData(b));

    private static UptickRecord Document(string id, string path, byte[] bytes, string? etag = null) => new()
    {
        Resource = "documentfiles", UptickId = id, Type = "DocumentFile",
        Attributes = $$$"""{"path":"{{{path}}}","size":{{{bytes.Length}}},"metadata":{"etag":"\"{{{etag ?? Md5(bytes)}}}\""}}""",
        CreatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static UptickRecord Report(string id, DateTime updated) => new()
    {
        Resource = "reports", UptickId = id, Type = "Report", Attributes = "{}", UpdatedUtc = updated
    };

    private static readonly DateTime Oct1 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Setup(UptickFileDownloader Downloader, Mock<IUptickApiClient> Api, Storage Storage, MemoryStore Store);

    private static Setup Create(FyreApp.Data.AppDbContext db)
    {
        var storage = new Storage();
        storage.Files["/fyrepower/properties/1/photos/items/2/remarks/3/a.jpg"] = Photo;
        storage.Files["/fyrepower/tasks/46515/InspectionReceipts/Rudi.png"] = Signature;
        storage.Files["/fyrepower/reports/9999/R-09999.pdf"] = Pdf;

        var api = new Mock<IUptickApiClient>();
        api.Setup(a => a.DocumentDownloadUrlAsync("photo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Uri("https://uptick-customer-storage.s3.amazonaws.com/fyrepower/properties/1/photos/items/2/remarks/3/a.jpg?sig=1"));
        api.Setup(a => a.DocumentDownloadUrlAsync("sig-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Uri("https://uptick-customer-storage.s3.amazonaws.com/fyrepower/tasks/46515/InspectionReceipts/Rudi.png?sig=1"));
        api.Setup(a => a.PdfAsync("reports", "9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UptickPdf("fyrepower/reports/9999/R-09999.pdf", Pdf.Length, null,
                new Uri("https://uptick-customer-storage.s3.amazonaws.com/fyrepower/reports/9999/R-09999.pdf?sig=1")));
        api.Setup(a => a.PdfAsync("reports", "10", It.IsAny<CancellationToken>())).ReturnsAsync((UptickPdf?)null);

        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(UptickFileDownloader.HttpClientName)).Returns(() => new HttpClient(storage, disposeHandler: false));
        var store = new MemoryStore();
        return new Setup(new UptickFileDownloader(db, api.Object, store, http.Object, NullLogger<UptickFileDownloader>.Instance), api, storage, store);
    }

    private static void AskedNothing(Mock<IUptickApiClient> api)
    {
        api.Verify(a => a.DocumentDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(a => a.PdfAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<List<UptickEndpointResult>> RunAsync(UptickFileDownloader downloader)
    {
        var results = new List<UptickEndpointResult>();
        await downloader.DownloadAsync(results, null, CancellationToken.None);
        return results;
    }

    [Fact]
    public async Task Invoice_and_quote_pdfs_are_stored_with_their_record()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(
            new UptickRecord { Resource = "invoices", UptickId = "52965", Type = "Invoice", Attributes = "{}", UpdatedUtc = Oct1 },
            new UptickRecord { Resource = "defectquotes", UptickId = "345", Type = "DefectQuote", Attributes = "{}", UpdatedUtc = Oct1 });
        await db.SaveChangesAsync();
        var s = Create(db);
        var invoicePdf = Encoding.UTF8.GetBytes("%PDF invoice");
        s.Storage.Files["/fyrepower/invoices/52965/IV000032831.pdf"] = invoicePdf;
        s.Api.Setup(a => a.PdfAsync("invoices", "52965", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UptickPdf("fyrepower/invoices/52965/IV000032831.pdf", invoicePdf.Length, null,
                new Uri("https://uptick-customer-storage.s3.amazonaws.com/fyrepower/invoices/52965/IV000032831.pdf?sig=1")));
        s.Api.Setup(a => a.PdfAsync("defectquotes", "345", It.IsAny<CancellationToken>())).ReturnsAsync((UptickPdf?)null);

        var results = await RunAsync(s.Downloader);

        Assert.Equal(invoicePdf, s.Store.Files["uptick/invoices/52965.pdf"].Bytes);
        var invoice = await db.UptickFiles.SingleAsync(f => f.Source == "invoices");
        Assert.Equal(("invoices/52965/IV000032831.pdf", "invoices", 52965L, FyreApp.Services.Uptick.Files.UptickFilePaths.Pdf),
            (invoice.Path, invoice.RecordType, invoice.RecordUptickId!.Value, invoice.Kind));
        Assert.True((await db.UptickFiles.SingleAsync(f => f.Source == "defectquotes")).NoFile);
        Assert.Contains("1 quotes have no PDF", results.Single(r => r.Resource == "defectquotes.files").Note);
        Assert.Equal(["reports.files", "invoices.files", "defectquotes.files", "servicequotes.files", "documentfiles.files"],
            results.Select(r => r.Resource));
    }

    [Fact]
    public async Task Report_pdfs_and_document_files_are_stored_and_not_fetched_again()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(
            Document("photo-1", "properties/1/photos/items/2/remarks/3/a.jpg", Photo),
            Document("sig-1", "tasks/46515/InspectionReceipts/Rudi.png", Signature),
            Report("9999", Oct1), Report("10", Oct1));
        await db.SaveChangesAsync();
        var s = Create(db);

        var results = await RunAsync(s.Downloader);

        Assert.Equal(Photo, s.Store.Files["uptick/documentfiles/photo-1.jpg"].Bytes);
        Assert.Equal("image/jpeg", s.Store.Files["uptick/documentfiles/photo-1.jpg"].Type);
        Assert.Equal(Signature, s.Store.Files["uptick/documentfiles/sig-1.png"].Bytes);
        Assert.Equal(Pdf, s.Store.Files["uptick/reports/9999.pdf"].Bytes);
        Assert.Equal("application/pdf", s.Store.Files["uptick/reports/9999.pdf"].Type);

        var report = await db.UptickFiles.SingleAsync(f => f.Source == "reports" && f.SourceId == "9999");
        Assert.Equal(("reports/9999/R-09999.pdf", (long)Pdf.Length, Md5(Pdf)), (report.Path, report.Size!.Value, report.Md5));
        var none = await db.UptickFiles.SingleAsync(f => f.Source == "reports" && f.SourceId == "10");
        Assert.True(none.NoFile);
        Assert.Null(none.StorageKey);

        var reports = results.Single(r => r.Resource == "reports.files");
        Assert.Equal((2, 1, 1), (reports.Received, reports.Created, reports.Copied));
        Assert.Contains("1 reports have no PDF", reports.Note);
        Assert.Equal((2, 2), (results.Single(r => r.Resource == "documentfiles.files").Created, results.Single(r => r.Resource == "documentfiles.files").Copied));

        // Nothing changed in Uptick: the next run asks it for nothing
        s.Api.Invocations.Clear();
        var again = await RunAsync(s.Downloader);
        AskedNothing(s.Api);
        Assert.All(again, r => Assert.Equal((0, r.UptickCount!.Value), (r.Received, r.Unchanged)));
    }

    [Fact]
    public async Task A_file_changed_in_Uptick_is_fetched_again_and_a_changed_report_is_checked_again()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(Document("photo-1", "properties/1/photos/items/2/remarks/3/a.jpg", Photo), Report("10", Oct1));
        await db.SaveChangesAsync();
        var s = Create(db);
        await RunAsync(s.Downloader);

        var replaced = Encoding.UTF8.GetBytes("a retaken photo");
        s.Storage.Files["/fyrepower/properties/1/photos/items/2/remarks/3/a.jpg"] = replaced;
        (await db.UptickRecords.SingleAsync(r => r.UptickId == "photo-1")).Attributes =
            Document("photo-1", "properties/1/photos/items/2/remarks/3/a.jpg", replaced).Attributes;
        (await db.UptickRecords.SingleAsync(r => r.UptickId == "10")).UpdatedUtc = Oct1.AddDays(1);
        await db.SaveChangesAsync();
        s.Api.Invocations.Clear();

        var results = await RunAsync(s.Downloader);

        Assert.Equal(replaced, s.Store.Files["uptick/documentfiles/photo-1.jpg"].Bytes);
        Assert.Equal(1, results.Single(r => r.Resource == "documentfiles.files").Updated);
        s.Api.Verify(a => a.PdfAsync("reports", "10", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(Oct1.AddDays(1), (await db.UptickFiles.SingleAsync(f => f.SourceId == "10")).SourceUpdatedUtc);
    }

    [Fact]
    public async Task A_download_that_does_not_match_Uptick_is_not_stored_and_is_given_up_on_after_three_runs()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.Add(Document("photo-1", "properties/1/photos/items/2/remarks/3/a.jpg", Photo, etag: new string('0', 32)));
        await db.SaveChangesAsync();
        var s = Create(db);

        var results = await RunAsync(s.Downloader);

        Assert.Empty(s.Store.Files);
        var row = await db.UptickFiles.SingleAsync();
        Assert.Equal((1, "The download doesn't match Uptick's MD5.", (DateTime?)null), (row.Failures, row.Error, row.DownloadedUtc));
        Assert.Contains("photo-1: The download doesn't match Uptick's MD5.", results.Single(r => r.Resource == "documentfiles.files").Warning);

        await RunAsync(s.Downloader);
        await RunAsync(s.Downloader);
        s.Api.Invocations.Clear();
        await RunAsync(s.Downloader);
        AskedNothing(s.Api);
        Assert.Equal(UptickFileDownloader.MaxFailures, (await db.UptickFiles.SingleAsync()).Failures);
    }

    [Fact]
    public async Task Nothing_runs_without_file_storage()
    {
        await using var db = DbContextFactory.Create();
        var s = Create(db);
        s.Store.IsConfigured = false;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(s.Downloader));
        Assert.Contains("Storage:Provider", ex.Message);
    }

    [Fact]
    public async Task The_local_store_keeps_files_under_its_folder_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "fyreapp-store-" + Guid.NewGuid());
        try
        {
            var store = new LocalFileStore(root);
            await store.PutAsync("uptick/reports/1.pdf", new MemoryStream(Pdf), "application/pdf");

            await using (var read = await store.OpenReadAsync("uptick/reports/1.pdf"))
            {
                using var copy = new MemoryStream();
                await read!.CopyToAsync(copy);
                Assert.Equal(Pdf, copy.ToArray());
            }
            Assert.Null(await store.OpenReadAsync("uptick/reports/2.pdf"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync("../outside.txt", new MemoryStream(Pdf), null));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("properties/5508/photos/items/24794/remarks/55400/2794a389.jpg", "remarks", 55400L, "photo")]
    [InlineData("properties/1008/photos/items/1216/20260130/b2569615.jpg", "assets", 1216L, "photo")]
    [InlineData("properties/1/photos/logbooks/logbook_1/entry_1/fb54c93a.jpg", "properties", 1L, "photo")]
    [InlineData("properties/1002/Dispatch/2021/Combined/P-2002-2021-07-19-0.pdf", "properties", 1002L, "document")]
    [InlineData("tasks/46515/InspectionReceipts/2026_10_20_134159_many.png", "tasks", 46515L, "signature")]
    [InlineData("tasks/10048/Work Order.pdf", "tasks", 10048L, "document")]
    [InlineData("clients/10164/Client Information Form NEW 2024.pdf", "clients", 10164L, "document")]
    [InlineData("reports/2456/R-02456 P-2327 Service Report.pdf", "reports", 2456L, "document")]
    [InlineData("quoting/defectquotes/1503/UploadedAuthorisation.pdf", "defectquotes", 1503L, "document")]
    [InlineData("dynamicforms/56/Queensland-Form-72_10-01-22_56.pdf", "dynamicformresponses", 56L, "document")]
    public void A_file_belongs_to_the_record_its_path_names(string path, string type, long id, string kind) =>
        Assert.Equal(new UptickFilePaths.Owner(type, id, kind), UptickFilePaths.Classify(path));

    [Fact]
    public void Account_level_files_belong_to_no_record() =>
        Assert.Null(UptickFilePaths.Classify("contract/Fyrepower - V2 SIGNED Uptick Software Agreement.pdf"));

    [Fact]
    public async Task Every_listed_file_is_recorded_with_its_record_and_pages_show_what_is_held()
    {
        await using var db = DbContextFactory.Create();
        var photo = Document("photo-1", "properties/1/photos/items/2/remarks/3/a.jpg", Photo);
        photo.Attributes = photo.Attributes.Replace("\"metadata\":{", "\"metadata\":{\"remark-photo-type\":\"open\",");
        db.UptickRecords.AddRange(photo, Document("photo-2", "properties/1/photos/items/2/remarks/3/b.jpg", Photo),
            Document("sig-1", "tasks/46515/InspectionReceipts/Rudi.png", Signature), Report("9999", Oct1),
            new UptickRecord { Resource = "taskclientsignatures", UptickId = "40724",
                Attributes = """{"image_path":"tasks/46515/InspectionReceipts/Rudi.png","signee_name":"Rudi","no_signature_reason":""}""" });
        await db.SaveChangesAsync();
        var s = Create(db);

        // A trial of one of each: every document file is still recorded, with the record it belongs to
        var results = new List<UptickEndpointResult>();
        await s.Downloader.DownloadAsync(results, null, CancellationToken.None, limit: 1);

        Assert.Equal(3, await db.UptickFiles.CountAsync(f => f.Source == "documentfiles"));
        var first = await db.UptickFiles.SingleAsync(f => f.SourceId == "photo-1");
        Assert.Equal(("remarks", 3L, "photo", "open"), (first.RecordType, first.RecordUptickId!.Value, first.Kind, first.PhotoType));
        Assert.Equal("Rudi", (await db.UptickFiles.SingleAsync(f => f.SourceId == "sig-1")).Caption);

        var remark = await FyreApp.Infrastructure.UptickFilesViewComponent.LoadAsync(db, "remarks", 3);
        Assert.Equal((1, 1), (remark.Photos.Count, remark.Waiting));
        Assert.Equal(("a.jpg", "open"), (remark.Photos[0].Name, remark.Photos[0].PhotoType));
        var task = await FyreApp.Infrastructure.UptickFilesViewComponent.LoadAsync(db, "tasks", 46515);
        Assert.Equal((0, 1), (task.Signatures.Count, task.Waiting));

        // The rest, then the report's PDF link
        await RunAsync(s.Downloader);
        task = await FyreApp.Infrastructure.UptickFilesViewComponent.LoadAsync(db, "tasks", 46515);
        Assert.Equal(("Rudi", 0), (Assert.Single(task.Signatures).Caption, task.Waiting));
        var pdfs = await UptickFileLinks.PdfsAsync(db, "reports", [9999L, 10L]);
        Assert.Equal((await db.UptickFiles.SingleAsync(f => f.Source == "reports")).Id, Assert.Single(pdfs).Value);
    }

    [Fact]
    public async Task The_storage_test_writes_a_file_and_reads_it_back()
    {
        var store = new MemoryStore();
        var (ok, message) = await FileStores.TestAsync(store);
        Assert.True(ok, message);
        Assert.True(store.Files.ContainsKey(FileStores.TestKey));

        store.IsConfigured = false;
        Assert.False((await FileStores.TestAsync(store)).Ok);
    }
}
