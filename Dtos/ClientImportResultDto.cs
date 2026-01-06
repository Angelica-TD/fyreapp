namespace FyreApp.Dtos;

public class ClientImportResultDto
{
    public int TotalRows { get; set; }
    public int Created { get; set; }

    public int SkippedDuplicateExternalId { get; set; }
    public int SkippedDuplicateName { get; set; }
    public int SkippedMissingName { get; set; }

    // row-level invalid data (length, format, etc)
    public int SkippedInvalid { get; set; }

    // Fatal errors ONLY
    public int Failed { get; set; }

    public int SkippedDuplicateExternalIdInFile { get; set; }
    public int SkippedDuplicateExternalIdMultipleHighPropertyCount { get; set; }

    public List<string> Messages { get; set; } = new();
}
