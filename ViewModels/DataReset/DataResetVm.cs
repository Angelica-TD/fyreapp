namespace FyreApp.ViewModels.DataReset;

public class DataResetVm
{
    public bool Enabled { get; set; }
    public IReadOnlyList<(string Table, int Count)> Counts { get; set; } = Array.Empty<(string, int)>();

    // Set after a reset has just run
    public IReadOnlyList<(string Table, int Count)>? Deleted { get; set; }
}
