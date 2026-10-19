using FyreApp.Models;

namespace FyreApp.ViewModels.Proposals;

public class ProposalDetailsVm
{
    public Proposal Quote { get; set; } = null!;
    public UpdateProposalRequest Edit { get; set; } = new();
    public bool OpenEdit { get; set; }
    public List<MaintenanceInterval> Intervals { get; set; } = new();
}
