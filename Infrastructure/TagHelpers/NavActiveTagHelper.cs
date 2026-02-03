using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FyreApp.Infrastructure.TagHelpers;

[HtmlTargetElement("a", Attributes = "nav-active")]
public class NavActiveTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    // Optional: allow overriding match behaviour
    [HtmlAttributeName("nav-active-controller-only")]
    public bool ControllerOnly { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var currentController = ViewContext.RouteData.Values["controller"]?.ToString();
        var currentAction = ViewContext.RouteData.Values["action"]?.ToString();

        // These are set by the built-in AnchorTagHelper (asp-controller/asp-action)
        context.AllAttributes.TryGetAttribute("asp-controller", out var aspControllerAttr);
        context.AllAttributes.TryGetAttribute("asp-action", out var aspActionAttr);

        var targetController = aspControllerAttr?.Value?.ToString();
        var targetAction = aspActionAttr?.Value?.ToString() ?? "Index";

        var isActive = !string.IsNullOrWhiteSpace(targetController)
            && string.Equals(currentController, targetController, StringComparison.OrdinalIgnoreCase)
            && (ControllerOnly || string.Equals(currentAction, targetAction, StringComparison.OrdinalIgnoreCase));

        if (isActive)
        {
            var existingClass = output.Attributes["class"]?.Value?.ToString() ?? string.Empty;
            var newClass = string.IsNullOrWhiteSpace(existingClass)
                ? "active"
                : $"{existingClass} active";

            output.Attributes.SetAttribute("class", newClass);
        }

        // Remove marker attribute from rendered HTML
        output.Attributes.RemoveAll("nav-active");
        output.Attributes.RemoveAll("nav-active-controller-only");
    }
}
