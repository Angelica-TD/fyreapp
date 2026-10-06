namespace FyreApp.Infrastructure;

public static class ImportLimits
{
    // Largest export upload accepted by the import pages. CSV exports are several times bigger than
    // XLSX (the tasks CSV is ~21 MB and a routines CSV can pass 20 MB), so leave plenty of room.
    // An upload over the limit is cut off by the server, which browsers / proxies report as a bare
    // HTTP 400, so the file inputs also check the size first (data-max-bytes, see site.js).
    public const long MaxUploadBytes = 100_000_000;
}
