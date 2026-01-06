namespace FyreApp.Dtos;

public class ClientImportProgressDto
{
    public string ImportId { get; set; } = "";
    public int Total { get; set; }
    public int Processed { get; set; }

    public int WouldCreate { get; set; }

    public int SkippedDuplicateExternalId { get; set; }
    public int SkippedDuplicateName { get; set; }
    public int SkippedMissingName { get; set; }

    public int SkippedInvalid { get; set; }

    public int Failed { get; set; }

    public bool DryRun { get; set; }
    public bool Completed { get; set; }
    public string? Message { get; set; }

    public List<string> ReportMessages { get; set; } = new();
    public List<ClientImportIssueDto> Issues { get; set; } = new();
}
