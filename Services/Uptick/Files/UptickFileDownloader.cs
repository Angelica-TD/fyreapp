using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Files;
using FyreApp.Services.Uptick.Api;
using FyreApp.Services.Uptick.Migration;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Files;

// Copies the files Uptick stores into FyreApp's file store (docs/uptick-migration.md §8): the PDFs Uptick makes
// (reports, invoices, defect quotes, sales quotes) and document files (photos, signatures, attachments), as listed in the raw copy. One API request each (the PDF's or the
// file's download link, a signed link to Uptick's storage fetched without the token), so a first run takes hours;
// it's resumable: what's held and unchanged is skipped. Each file is checked against Uptick's size and MD5 before
// it's stored.
public class UptickFileDownloader
{
    public const string HttpClientName = "uptick-files";

    public const string Documents = "documentfiles";
    public const string Reports = "reports";
    public const string Invoices = UptickResources.Invoices;
    public const string DefectQuotes = UptickResources.DefectQuotes;
    public const string ServiceQuotes = UptickResources.ServiceQuotes;

    // The records Uptick makes a PDF of (`<resource>/<id>/pdf/`), with what they're called
    internal static readonly (string Source, string Noun)[] PdfSources =
        [(Reports, "report"), (Invoices, "invoice"), (DefectQuotes, "quote"), (ServiceQuotes, "sales quote")];

    public static bool IsPdf(string source) => PdfSources.Any(p => p.Source == source);

    // A file that failed this many runs isn't tried again until it changes in Uptick
    internal const int MaxFailures = 3;

    private const int MaxErrorsListed = 10;

    // Failures in a row that stop the run: something is wrong beyond one file (login, network, Uptick)
    internal const int MaxFailuresInARow = 25;

    private readonly AppDbContext _db;
    private readonly IUptickApiClient _api;
    private readonly IFileStore _store;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<UptickFileDownloader> _logger;
    private readonly TimeProvider _clock;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public UptickFileDownloader(AppDbContext db, IUptickApiClient api, IFileStore store, IHttpClientFactory http,
        ILogger<UptickFileDownloader> logger, TimeProvider? clock = null)
    {
        _db = db;
        _api = api;
        _store = store;
        _http = http;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // One raw record that may have a file: a document file (with Uptick's path, size, etag and, for a remark photo,
    // open / closed) or a record Uptick makes a PDF of
    private sealed record Candidate(string Id, string? Path, long? Size, string? Etag, DateTime? UpdatedUtc, string? PhotoType = null,
        DateTime? CreatedUtc = null);

    // At most limit files of each kind when given: a trial before the full download
    public async Task DownloadAsync(List<UptickEndpointResult> results, IProgress<UptickProgress>? progress, CancellationToken ct,
        int? limit = null)
    {
        if (!_store.IsConfigured) throw new InvalidOperationException("File storage isn't set up (Storage:Provider).");

        var sources = PdfSources
            .Select(p => (p.Source, Label: $"{p.Noun} PDFs", Load: (Func<CancellationToken, Task<List<Candidate>>>)(c => PdfCandidatesAsync(p.Source, c))))
            .Append((Documents, "document files", DocumentsAsync))
            .ToArray();
        for (var i = 0; i < sources.Length; i++)
        {
            var (source, label, load) = sources[i];
            var result = new UptickEndpointResult { Resource = source + ".files", Mode = "Files", Status = "Ok" };
            results.Add(result);
            var watch = Stopwatch.StartNew();
            var requestsBefore = _api.Requests;
            try
            {
                await DownloadSourceAsync(source, await load(ct), limit, result,
                    (done, total, bytes) => progress?.Report(new UptickProgress(source, i + 1, sources.Length, done, total, _api.Requests,
                        _api.Throttled, $"Downloading {label} ({i + 1}/{sources.Length}, {Size(bytes)})")), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Status = "Failed";
                result.Error = ex.Message;
                throw;
            }
            finally
            {
                result.Requests = _api.Requests - requestsBefore;
                result.Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
                result.Copied = await _db.UptickFiles.CountAsync(f => f.Source == source && f.DownloadedUtc != null, CancellationToken.None);
            }
        }
    }

    private async Task DownloadSourceAsync(string source, List<Candidate> candidates, int? limit, UptickEndpointResult result,
        Action<int, int, long> report, CancellationToken ct)
    {
        var held = await _db.UptickFiles.Where(f => f.Source == source).ToDictionaryAsync(f => f.SourceId, ct);
        if (source == Documents)
        {
            // Every document file listed, with the record it belongs to, before any is downloaded: a page can say
            // what's still to come. Etag is Uptick's as listed; a held file's stays the one downloaded until it is again.
            var signers = await SignersAsync(ct);
            foreach (var raw in candidates)
            {
                if (!held.TryGetValue(raw.Id, out var row))
                {
                    row = new UptickFile { Source = source, SourceId = raw.Id, Etag = raw.Etag, Size = raw.Size, CheckedUtc = Now };
                    _db.UptickFiles.Add(row);
                    held[raw.Id] = row;
                }
                Describe(row, raw);
                row.Caption = raw.Path != null && signers.TryGetValue(raw.Path, out var signer) ? Cap(signer, 300) : null;
            }
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // PDFs held from before files were linked to their records
            foreach (var row in held.Values.Where(r => r.RecordType == null))
                Describe(row);
            await _db.SaveChangesAsync(ct);
        }

        var work = new List<(Candidate Raw, UptickFile? Row)>();
        foreach (var raw in candidates)
        {
            held.TryGetValue(raw.Id, out var row);
            if (row != null && Changed(source, row, raw)) row.Failures = 0;
            if (row == null || NeedsWork(source, row, raw)) work.Add((raw, row));
        }
        result.UptickCount = candidates.Count;
        result.Unchanged = candidates.Count - work.Count;
        if (limit is { } max && work.Count > max)
        {
            result.Note = $"Trial: the first {max:N0} of {work.Count:N0} to download. ";
            work = work.Take(max).ToList();
        }

        long bytes = 0;
        var errors = new List<string>();
        var failed = 0;
        var inARow = 0;
        for (var n = 0; n < work.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            report(n, work.Count, bytes);
            var (raw, row) = work[n];
            var isNew = row?.DownloadedUtc == null;
            if (row == null)
            {
                row = new UptickFile { Source = source, SourceId = raw.Id };
                _db.UptickFiles.Add(row);
            }

            result.Received++;
            try
            {
                var stored = IsPdf(source) ? await PdfAsync(source, row, raw, ct) : await DocumentAsync(row, raw, ct);
                if (stored is { } size)
                {
                    bytes += size;
                    if (isNew) result.Created++; else result.Updated++;
                }
                row.Error = null;
                row.Failures = 0;
                inARow = 0;
            }
            catch (Exception ex) when (ex is FileCheckException or HttpRequestException or IOException
                                           or UptickApiException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                failed++;
                row.Error = Cap(ex.Message, 500);
                row.Failures++;
                if (errors.Count < MaxErrorsListed) errors.Add($"{raw.Id}: {ex.Message}");
                _logger.LogWarning("Uptick file {Source} {Id} failed: {Error}", source, raw.Id, ex.Message);
                inARow++;
            }
            row.CheckedUtc = Now;
            // Saved file by file, so a cancelled or cut-off run keeps what it did
            await _db.SaveChangesAsync(CancellationToken.None);
            if (inARow >= MaxFailuresInARow)
                throw new InvalidOperationException($"Stopped after {inARow} files in a row failed; the last: {row.Error}");
        }
        report(work.Count, work.Count, bytes);

        result.Note += $"{Size(bytes)} downloaded";
        if (PdfSources.FirstOrDefault(p => p.Source == source) is { Noun: { } noun })
        {
            var none = await _db.UptickFiles.CountAsync(f => f.Source == source && f.NoFile, ct);
            if (none > 0) result.Note += $"; {none:N0} {noun}s have no PDF in Uptick";
        }
        if (failed > 0) result.Warning = $"{failed:N0} failed (tried again next run, up to {MaxFailures} times): {string.Join("; ", errors)}";
    }

    // Uptick's copy differs from the one held: a new etag, or a record changed since its PDF was checked
    private static bool Changed(string source, UptickFile row, Candidate raw) => IsPdf(source)
        ? raw.UpdatedUtc != null && raw.UpdatedUtc != row.SourceUpdatedUtc
        : raw.Etag != null && !string.Equals(raw.Etag, row.Etag, StringComparison.Ordinal);

    private static bool NeedsWork(string source, UptickFile row, Candidate raw)
    {
        if (Changed(source, row, raw)) return true;
        if (row.DownloadedUtc != null || row.NoFile) return false;
        return row.Failures < MaxFailures;
    }

    // A record's PDF: Uptick's pdf link, if it has made one. Returns the bytes stored, or null when it has none.
    private async Task<long?> PdfAsync(string source, UptickFile row, Candidate raw, CancellationToken ct)
    {
        var pdf = await _api.PdfAsync(source, raw.Id, ct);
        row.SourceUpdatedUtc = raw.UpdatedUtc;
        Describe(row);
        if (pdf == null)
        {
            row.NoFile = true;
            return null;
        }
        row.NoFile = false;
        // Uptick's key starts with the account ("fyrepower/reports/…", "fyrepower/quotes/…")
        row.Path = Cap(pdf.Key is { } key && key.IndexOf('/') is var slash and > 0 ? key[(slash + 1)..] : pdf.Key, 1000);
        return await FetchAndStoreAsync(row, pdf.Url, $"uptick/{source}/{raw.Id}.pdf", pdf.Size, md5: null, "application/pdf", ct);
    }

    // Where Uptick keeps a document file, and so the record it belongs to
    private static void Describe(UptickFile row, Candidate raw)
    {
        row.Path = Cap(raw.Path, 1000);
        var owner = UptickFilePaths.Classify(raw.Path);
        row.RecordType = owner?.RecordType;
        row.RecordUptickId = owner?.RecordUptickId;
        row.Kind = owner?.Kind ?? UptickFilePaths.Document;
        row.PhotoType = Cap(raw.PhotoType, 20);
        row.CreatedUtc = raw.CreatedUtc;
    }

    // A PDF belongs to its record (a report, an invoice, …)
    private static void Describe(UptickFile pdf)
    {
        pdf.RecordType = pdf.Source;
        pdf.RecordUptickId = long.TryParse(pdf.SourceId, out var id) ? id : null;
        pdf.Kind = UptickFilePaths.Pdf;
    }

    // Customers' signatures by image path: who signed, or why there's no signature
    private async Task<Dictionary<string, string>> SignersAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attributes in await _db.UptickRecords.AsNoTracking()
                     .Where(r => r.Resource == "taskclientsignatures" && r.DeletedUtc == null)
                     .Select(r => r.Attributes).ToListAsync(ct))
        {
            using var doc = JsonDocument.Parse(attributes);
            var a = doc.RootElement;
            string? Text(string name) => a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim() : null;
            if (Text("image_path") is { } path && (Text("signee_name") ?? (Text("no_signature_reason") is { } why ? $"Not signed: {why}" : null)) is { } who)
                map[path] = who;
        }
        return map;
    }

    private async Task<long?> DocumentAsync(UptickFile row, Candidate raw, CancellationToken ct)
    {
        row.Etag = raw.Etag;
        var url = await _api.DocumentDownloadUrlAsync(raw.Id, ct) ?? throw new FileCheckException("Uptick has no such file.");
        var extension = System.IO.Path.GetExtension(raw.Path ?? "");
        if (extension.Length > 10 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.')) extension = "";
        var md5 = raw.Etag?.Trim('"') is { Length: 32 } tag && tag.All(Uri.IsHexDigit) ? tag.ToLowerInvariant() : null;
        return await FetchAndStoreAsync(row, url, $"uptick/documentfiles/{raw.Id}{extension.ToLowerInvariant()}", raw.Size, md5,
            ContentTypes.TryGetContentType(raw.Path ?? "", out var type) ? type : null, ct);
    }

    // Downloads to a temporary file, checks size and MD5, then stores it
    private async Task<long> FetchAndStoreAsync(UptickFile row, Uri url, string key, long? size, string? md5, string? contentType,
        CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps) throw new FileCheckException("Uptick's download link isn't HTTPS.");

        var temp = System.IO.Path.GetTempFileName();
        try
        {
            string hash;
            long length;
            string? servedType;
            using (var response = await _http.CreateClient(HttpClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    throw new FileCheckException($"Uptick's storage returned {(int)response.StatusCode}.");
                servedType = response.Content.Headers.ContentType?.MediaType;
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(temp);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                var buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                length = file.Length;
                hash = Convert.ToHexStringLower(hasher.GetHashAndReset());
            }

            if (size is { } expected && expected != length)
                throw new FileCheckException($"Downloaded {length:N0} bytes, Uptick lists {expected:N0}.");
            if (md5 != null && md5 != hash)
                throw new FileCheckException("The download doesn't match Uptick's MD5.");

            var type = contentType ?? (servedType is { } t && t != "application/octet-stream" ? t : null);
            await using (var stored = File.OpenRead(temp))
                await _store.PutAsync(key, stored, type, ct);

            row.StorageKey = key;
            row.Size = length;
            row.Md5 = hash;
            row.ContentType = Cap(type, 200);
            row.DownloadedUtc = Now;
            return length;
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private async Task<List<Candidate>> PdfCandidatesAsync(string source, CancellationToken ct) =>
        (await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == source && r.DeletedUtc == null)
            .Select(r => new { r.UptickId, r.UpdatedUtc })
            .ToListAsync(ct))
        .OrderByDescending(r => long.TryParse(r.UptickId, out var id) ? id : 0)
        .Select(r => new Candidate(r.UptickId, null, null, null, r.UpdatedUtc))
        .ToList();

    private async Task<List<Candidate>> DocumentsAsync(CancellationToken ct)
    {
        var rows = await _db.UptickRecords.AsNoTracking()
            .Where(r => r.Resource == Documents && r.DeletedUtc == null)
            .OrderByDescending(r => r.CreatedUtc)
            .Select(r => new { r.UptickId, r.Attributes, r.UpdatedUtc, r.CreatedUtc })
            .ToListAsync(ct);
        var list = new List<Candidate>(rows.Count);
        foreach (var r in rows)
        {
            using var doc = JsonDocument.Parse(r.Attributes);
            var a = doc.RootElement;
            string? path = a.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            long? size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : null;
            var metadata = a.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
            string? Meta(string name) => metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(name, out var v)
                && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            list.Add(new Candidate(r.UptickId, path, size, Meta("etag"), r.UpdatedUtc, Meta("remark-photo-type"), r.CreatedUtc));
        }
        return list;
    }

    private static string? Cap(string? s, int max) => s == null || s.Length <= max ? s : s[..max];

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        _ => $"{bytes / 1024.0:0} KB"
    };

    private sealed class FileCheckException(string message) : Exception(message);
}
