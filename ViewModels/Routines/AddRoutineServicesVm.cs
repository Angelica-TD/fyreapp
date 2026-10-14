namespace FyreApp.ViewModels.Routines;

// Uptick's "Add routine services" modal on a property's Routines tab: dates and subcontractor, then the
// routine types to add (search, standard filter) with the levels to do for each
public class AddRoutineServicesVm
{
    public int SiteId { get; set; }
    public IReadOnlyList<RoutineTypeOptionVm> Types { get; set; } = [];
    public IReadOnlyList<string> Standards { get; set; } = [];
    public IReadOnlyList<string> Contractors { get; set; } = [];
}

public class RoutineTypeOptionVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Under the name, as Uptick shows it: "Section 2", "AS1851-2012 Section 14" or "Default"
    public string? Reference { get; set; }
    public string? Standard { get; set; }

    public IReadOnlyList<RoutineLevelOptionVm> Levels { get; set; } = [];

    // Active assets at the property whose asset type lists this routine as applicable
    public int ApplicableAssets { get; set; }

    // Routines ("<type>: <level>") of this type the property already has
    public int ExistingRoutines { get; set; }
}

public class RoutineLevelOptionVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Ticked when the routine type is ticked (Uptick's "Default Enabled?")
    public bool DefaultEnabled { get; set; }
}

// One routine service on the property's Routines tab
public class RoutineServiceSummaryVm
{
    public int Id { get; set; }
    public string RoutineType { get; set; } = string.Empty;
    public IReadOnlyList<string> Levels { get; set; } = [];
    public DateTime StartDate { get; set; }
    public DateTime LastAnnualDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Subcontractor { get; set; }

    // Forecast routines still waiting for a task, and when the next is due
    public int PendingRoutines { get; set; }
    public DateTime? NextDue { get; set; }
}

// Edit routine service page
public class EditRoutineServiceVm
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string RoutineType { get; set; } = string.Empty;
    public IReadOnlyList<RoutineLevelOptionVm> Levels { get; set; } = [];
    public IReadOnlyList<string> Contractors { get; set; } = [];
    public EditRoutineServiceInput Input { get; set; } = new();
}

public class EditRoutineServiceInput
{
    public DateTime? StartDate { get; set; }
    public DateTime? LastAnnualDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Subcontractor { get; set; }
    public List<int> Levels { get; set; } = new();
}

// What the modal posts
public class AddRoutineServicesInput
{
    public DateTime? StartDate { get; set; }
    public DateTime? LastAnnualDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Subcontractor { get; set; }

    // The ticked levels; their routine types are the routine services added
    public List<int> Levels { get; set; } = new();
}
