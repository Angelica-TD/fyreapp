using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// A routine set up on a property in FyreApp (Uptick's "Add routine services"): a routine type, e.g.
// "14 - Emergency planning in facilities", with the levels done there (Six-monthly, Annual), from a start
// date, counted from the last annual service. Its occurrences are forecast as RoutineOccurrences, which the
// property's Routines tab and the Routines page show alongside the ones imported from Uptick.
public class PropertyRoutineService
{
    public int Id { get; set; }

    public int PropertyId { get; set; }
    public Property Property { get; set; } = null!;

    public int RoutineServiceTypeId { get; set; }
    public RoutineServiceType RoutineServiceType { get; set; } = null!;

    // The levels done at this property (many-to-many)
    public ICollection<RoutineServiceLevel> Levels { get; set; } = new List<RoutineServiceLevel>();

    // Calendar dates (UTC midnight). Nothing is forecast before the start date or after the end date;
    // levels fall due counting from the last annual date.
    public DateTime StartDate { get; set; }
    public DateTime LastAnnualDate { get; set; }
    public DateTime? EndDate { get; set; }

    // The contractor doing it, if subcontracted
    [StringLength(200)]
    public string? Subcontractor { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [StringLength(200)]
    public string? CreatedBy { get; set; }

    public ICollection<RoutineOccurrence> Occurrences { get; set; } = new List<RoutineOccurrence>();
}
