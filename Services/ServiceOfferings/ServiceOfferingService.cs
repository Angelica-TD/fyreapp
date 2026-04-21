using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.ServiceOfferings;

public class ServiceOfferingService : IServiceOfferingService
{
    private readonly AppDbContext _db;

    public ServiceOfferingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<ServiceOfferingViewModel>> GetAllAsync()
    {
        return await _db.ServiceOfferings
            .Include(s => s.Intervals)
            .OrderBy(s => s.Name)
            .Select(s => ToViewModel(s))
            .ToListAsync();
    }

    public async Task<ServiceOfferingViewModel?> GetByIdAsync(int id)
    {
        var offering = await _db.ServiceOfferings
            .Include(s => s.Intervals)
            .FirstOrDefaultAsync(s => s.Id == id);

        return offering == null ? null : ToViewModel(offering);
    }

    public async Task<ServiceOfferingViewModel> CreateAsync(ServiceOfferingFormDto dto)
    {
        var intervals = await _db.MaintenanceIntervals
            .Where(i => dto.SelectedIntervalIds.Contains(i.Id))
            .ToListAsync();

        var offering = new ServiceOffering
        {
            Name = dto.Name,
            Description = dto.Description,
            IsActive = dto.IsActive,
            Intervals = intervals
        };

        _db.ServiceOfferings.Add(offering);
        await _db.SaveChangesAsync();

        return ToViewModel(offering);
    }

    public async Task<bool> UpdateAsync(int id, ServiceOfferingFormDto dto)
    {
        var offering = await _db.ServiceOfferings
            .Include(s => s.Intervals)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (offering == null) return false;

        var intervals = await _db.MaintenanceIntervals
            .Where(i => dto.SelectedIntervalIds.Contains(i.Id))
            .ToListAsync();

        offering.Name = dto.Name;
        offering.Description = dto.Description;
        offering.IsActive = dto.IsActive;
        offering.Intervals.Clear();
        foreach (var interval in intervals)
            offering.Intervals.Add(interval);

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var offering = await _db.ServiceOfferings.FindAsync(id);
        if (offering == null) return false;

        _db.ServiceOfferings.Remove(offering);
        await _db.SaveChangesAsync();
        return true;
    }

    private static ServiceOfferingViewModel ToViewModel(ServiceOffering s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        IsActive = s.IsActive,
        Intervals = s.Intervals.Select(i => new IntervalSummary
        {
            Id = i.Id,
            Name = i.Name,
            Months = i.Months
        }).OrderBy(i => i.Months).ToList()
    };
}
