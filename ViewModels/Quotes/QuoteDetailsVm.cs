using FyreApp.Models;

namespace FyreApp.ViewModels.Quotes;

public class QuoteDetailsVm
{
    public Quote Quote { get; set; } = null!;
    public EditQuoteVm Edit { get; set; } = new();
    public List<MaintenanceInterval> Intervals { get; set; } = new();
    public bool OpenEdit { get; set; }
}
