using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Infrastructure;

public static class Breadcrumbs
{
    public static List<(string Text, string? Url)> Build(Controller controller, params (string Text, string? Action, string? Controller, object? RouteValues)[] items)
    {
        var list = new List<(string Text, string? Url)>();

        foreach (var i in items)
        {
            string? url = null;

            if (!string.IsNullOrWhiteSpace(i.Action) && !string.IsNullOrWhiteSpace(i.Controller))
                url = controller.Url.Action(i.Action, i.Controller, i.RouteValues);

            list.Add((i.Text, url));
        }

        return list;
    }
}
