using FyreApp.Models;
using FyreApp.ViewModels.MaintenanceSchedules;

namespace FyreApp.Services.MaintenanceSchedules;

public enum ScheduleCompleteStatus { Success, NotFound, Inactive, InvalidInterval }

public interface IMaintenanceScheduleService
{
    Task<IReadOnlyList<MaintenanceScheduleListItemVm>> GetDueListAsync(MaintenanceScheduleFilter filter);
    Task<MaintenanceSchedule?> GetDetailsAsync(int id);
    Task<ClientTask> GenerateTaskAsync(int scheduleId, string createdByUserId);
    Task<ScheduleCompleteStatus> CompleteAsync(int scheduleId, string? notes);
}
