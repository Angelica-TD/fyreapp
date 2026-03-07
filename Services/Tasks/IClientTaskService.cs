using FyreApp.Models;
using FyreApp.ViewModels.Tasks;

namespace FyreApp.Services;

public interface IClientTaskService
{
    Task<IReadOnlyList<ClientTaskListItemVm>> GetAllAsync(string? statusFilter = null, string? clientFilter = null);
    Task<ClientTask?> GetByIdAsync(int id);
    Task<ClientTask> CreateAsync(CreateClientTaskVm vm, string createdByUserId);
    Task<(bool Found, string Title)> UpdateAsync(int id, EditClientTaskVm vm);
    Task<bool> DeleteAsync(int id);
}