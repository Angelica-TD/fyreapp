using FyreApp.ViewModels.Auth;

namespace FyreApp.ViewModels.Home;

public class HomeIndexVm
{
    public LoginVm Login { get; set; } = new();

    public bool ShowLogin { get; set; } = true;
}
