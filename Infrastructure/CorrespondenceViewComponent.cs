using FyreApp.Services.Lists;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Infrastructure;

// "Correspondence": a migrated record's emails in Uptick (the latest few, with a link to them all) and, for a task,
// its dispatches (invoice and reports sent to the customer). Admins only, like the emails' pages.
public class CorrespondenceViewComponent : ViewComponent
{
    private readonly ICorrespondenceListService _list;

    public CorrespondenceViewComponent(ICorrespondenceListService list) => _list = list;

    public async Task<IViewComponentResult> InvokeAsync(string resource, long? uptickId, int take = 10)
    {
        if (uptickId is not { } id || !User.IsInRole("Admin")) return Content("");
        var model = await _list.PanelAsync(resource, id, take);
        return model.Total + model.Dispatches.Count > 0 ? View(model) : Content("");
    }
}
