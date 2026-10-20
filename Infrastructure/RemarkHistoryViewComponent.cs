using FyreApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Infrastructure;

// A remark's history in Uptick (remarkevents): each time it was added, verified or resolved, by whom and on which task
public class RemarkHistoryViewComponent : ViewComponent
{
    private readonly AppDbContext _db;

    public RemarkHistoryViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync(int remarkId)
    {
        var events = await _db.RemarkEvents.AsNoTracking()
            .Include(e => e.UptickUser).Include(e => e.ClientTask)
            .Where(e => e.RemarkId == remarkId)
            .OrderBy(e => e.WhenUtc).ThenBy(e => e.UptickId)
            .ToListAsync();
        return events.Count > 0 ? View(events) : Content("");
    }
}
