using FyreApp.Models;
using FyreApp.Services.UserManagement;

namespace FyreApp.ViewModels.Lists;

// Users page: everyone in Uptick, with their FyreApp login, plus logins not yet linked to an Uptick user
public class UsersIndexVm
{
    public DirectoryListVm<UptickUserListItemVm> List { get; set; } = new();
    public IReadOnlyList<UserViewModel> Unlinked { get; set; } = [];

    // Uptick users without a login, for linking an unlinked login to
    public IReadOnlyList<UptickUser> Linkable { get; set; } = [];
}
