using System.Text.RegularExpressions;

namespace FyreApp.Services.Uptick.Files;

// Which record a file Uptick stores belongs to, from where it keeps it (docs/uptick-migration.md §8). The ids in a
// path are Uptick's: "properties/5508/photos/items/24794/remarks/55400/a.jpg" is remark 55400's photo (on asset
// 24794 at property 5508).
public static partial class UptickFilePaths
{
    public const string Photo = "photo";
    public const string Signature = "signature";
    public const string Document = "document";
    public const string Pdf = "pdf";

    public sealed record Owner(string RecordType, long RecordUptickId, string Kind);

    public static Owner? Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (RemarkPhoto().Match(path) is { Success: true } remark) return new(UptickResources.Remarks, Id(remark), Photo);
        if (AssetPhoto().Match(path) is { Success: true } asset) return new(UptickResources.Assets, Id(asset), Photo);
        if (PropertyPhoto().Match(path) is { Success: true } photo) return new(UptickResources.Properties, Id(photo), Photo);
        if (Signature_().Match(path) is { Success: true } signature) return new(UptickResources.Tasks, Id(signature), Signature);
        if (Folder().Match(path) is { Success: true } folder)
            return new(folder.Groups["type"].Value switch
            {
                "quoting/defectquotes" => "defectquotes",
                "dynamicforms" => "dynamicformresponses",
                var t => t
            }, Id(folder), Document);
        return null;
    }

    private static long Id(Match m) => long.Parse(m.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^properties/\d+/photos/items/\d+/remarks/(?<id>\d{1,18})/")]
    private static partial Regex RemarkPhoto();

    [GeneratedRegex(@"^properties/\d+/photos/items/(?<id>\d{1,18})/")]
    private static partial Regex AssetPhoto();

    // Logbook photos and others kept under the property's photos
    [GeneratedRegex(@"^properties/(?<id>\d{1,18})/photos/")]
    private static partial Regex PropertyPhoto();

    // A customer's sign-off on a task (Uptick's taskclientsignatures point at it by image_path)
    [GeneratedRegex(@"^tasks/(?<id>\d{1,18})/InspectionReceipts/")]
    private static partial Regex Signature_();

    [GeneratedRegex(@"^(?<type>properties|tasks|clients|reports|invoices|servicequotes|dynamicforms|quoting/defectquotes)/(?<id>\d{1,18})/")]
    private static partial Regex Folder();
}

// Uptick's resource names, as FyreApp records them
public static class UptickResources
{
    public const string Remarks = "remarks";
    public const string Assets = "assets";
    public const string Properties = "properties";
    public const string Tasks = "tasks";
    public const string Clients = "clients";
    public const string Reports = "reports";
}
