namespace FyreApp.ViewModels.Sites;

public sealed class CreatePropertyModalVm
{
    // Required for POST
    public int? ClientId { get; set; }
    public string ClientName { get; set; } = "client";

    // Reuse options
    public string ModalId { get; set; } = "createSiteModal";
    public string IdPrefix { get; set; } = "createSite"; // used to generate element ids safely

    // Routing
    public string FormController { get; set; } = "Property";
    public string FormAction { get; set; } = "Create";

    // If you want to hide the header text when you don't yet know the client
    public bool ShowClientNameInTitle { get; set; } = true;

    // Needed because the partial will load Google Places itself
    public string GoogleBrowserKey { get; set; } = "";
}