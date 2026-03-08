namespace FyreApp.Services.Techs;

public interface ITechService
{
    Task<TechCreateResult> CreateAsync(TechCreateDto dto);
    Task<TechUpdateResult> UpdateAsync(string id, TechEditDto dto);
    Task<TechDeactivateResult> DeactivateAsync(string id);
    Task<TechDeleteResult> DeleteAsync(string id);
    Task<IEnumerable<TechViewModel>> GetAllAsync();
    Task<TechViewModel?> GetByIdAsync(string id);
}