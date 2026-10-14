using System.ComponentModel.DataAnnotations;

namespace FyreApp.Models;

// An asset a routine task services, e.g. extinguisher "FE 3" for "10 - Portable and Wheeled Fire Extinguishers: Six-monthly"
// (the task's Work tab). Saved when the task is generated from routines, so the list stays as it was planned.
public class TaskAsset
{
    public int Id { get; set; }

    public int ClientTaskId { get; set; }
    public ClientTask ClientTask { get; set; } = null!;

    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    // The routine the asset is serviced for, as Uptick names it ("<routine service type>: <level>")
    [Required, StringLength(300)]
    public string Routine { get; set; } = string.Empty;
}
