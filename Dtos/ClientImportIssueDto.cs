namespace FyreApp.Dtos;

public class ClientImportIssueDto
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public List<int> Rows { get; set; } = new();
    public string Message { get; set; } = "";

    public List<string> IdValues { get; set; } = new();
}
