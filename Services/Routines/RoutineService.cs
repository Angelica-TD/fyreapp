using FyreApp.Data;
using FyreApp.ViewModels.Routines;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Routines;

public interface IRoutineService
{
    Task<RoutineListVm> SearchAsync(RoutineFilter filter, CancellationToken ct = default);
}

// Routine occurrences imported from Uptick, listed and filtered like Uptick's Routines page
public class RoutineService : IRoutineService
{
    private readonly AppDbContext _db;
    public RoutineService(AppDbContext db) => _db = db;

    public async Task<RoutineListVm> SearchAsync(RoutineFilter filter, CancellationToken ct = default)
    {
        var q = _db.RoutineOccurrences.AsNoTracking().AsQueryable();

        if (filter.ClientActive is bool clientActive)
            q = q.Where(o => o.Site.Client.Active == clientActive);

        // Due dates are calendar dates stored as UTC midnight
        if (filter.DueFrom is DateTime from)
        {
            var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            q = q.Where(o => o.DueDate >= fromUtc);
        }
        if (filter.DueTo is DateTime to)
        {
            var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            q = q.Where(o => o.DueDate < toUtc);
        }

        if (filter.PropertyStatus.Count > 0)
        {
            var statuses = filter.PropertyStatus.Select(s => s.ToUpperInvariant()).ToList();
            q = q.Where(o => o.Site.Status != null && statuses.Contains(o.Site.Status));
        }

        if (filter.Status.Count > 0)
        {
            var statuses = filter.Status.ToList();
            q = q.Where(o => statuses.Contains(o.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            q = q.Where(o =>
                o.Routine.ToLower().Contains(term) ||
                o.Site.Name.ToLower().Contains(term) ||
                (o.Site.ExternalId != null && o.Site.ExternalId.ToLower().Contains(term)) ||
                o.Site.Client.Name.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)RoutineListVm.PageSize)));
        filter.Page = page;

        var items = await q
            .OrderBy(o => o.Routine).ThenBy(o => o.DueDate).ThenBy(o => o.Site.ExternalId) // as Uptick lists them
            .Skip((page - 1) * RoutineListVm.PageSize)
            .Take(RoutineListVm.PageSize)
            .Select(o => new RoutineListItemVm
            {
                Id = o.Id,
                Routine = o.Routine,
                SiteId = o.SiteId,
                PropertyRef = o.Site.ExternalId ?? o.Site.FyreRef,
                PropertyName = o.Site.Name,
                ClientId = o.Site.ClientId,
                ClientName = o.Site.Client.Name,
                ClientContact = o.Site.Client.PrimaryContactName,
                DueDate = o.DueDate,
                Status = o.Status,
                MaintenanceScheduleId = o.MaintenanceScheduleId
            })
            .ToListAsync(ct);

        return new RoutineListVm { Filter = filter, Items = items, Total = total };
    }
}
