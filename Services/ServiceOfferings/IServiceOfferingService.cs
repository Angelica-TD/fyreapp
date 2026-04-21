namespace FyreApp.Services.ServiceOfferings;

public interface IServiceOfferingService
{
    Task<IEnumerable<ServiceOfferingViewModel>> GetAllAsync();
    Task<ServiceOfferingViewModel?> GetByIdAsync(int id);
    Task<ServiceOfferingViewModel> CreateAsync(ServiceOfferingFormDto dto);
    Task<bool> UpdateAsync(int id, ServiceOfferingFormDto dto);
    Task<bool> DeleteAsync(int id);
}
