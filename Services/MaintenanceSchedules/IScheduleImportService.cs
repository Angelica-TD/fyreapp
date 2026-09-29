using FyreApp.ViewModels.MaintenanceSchedules;

namespace FyreApp.Services.MaintenanceSchedules;

public interface IScheduleImportService
{
    // Imports an Uptick "Routine schedules" export (CSV or XLSX).
    Task<ScheduleImportResultVm> ImportUptickAsync(
        Stream stream,
        string fileName,
        bool dryRun,
        CancellationToken ct = default);
}
