namespace FyreApp.Dtos;

public class ClientImportResultDto
{
    public int TotalRows { get; set; }
    public int Created { get; set; }

    // Already imported and unchanged in Uptick since
    public int SkippedDuplicateExternalId { get; set; }

    // Already imported and changed in Uptick since: updated from this export
    public int Updated { get; set; }

    // Placeholder clients (created by the property import) filled in from this export
    public int FilledPlaceholders { get; set; }

    // row-level invalid data (length, format, etc)
    public int SkippedInvalid { get; set; }

    // Fatal errors ONLY
    public int Failed { get; set; }

    public int SkippedDuplicateExternalIdInFile { get; set; }
    public int SkippedDuplicateExternalIdMultipleHighPropertyCount { get; set; }

    public List<string> Messages { get; set; } = new();
}
