using FyreApp.Data;
using FyreApp.Infrastructure;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IDirectoryListService
{
    Task<DirectoryListVm<Contractor>> ContractorsAsync(DirectoryFilter filter, CancellationToken ct = default);
    Task<byte[]> ContractorsCsvAsync(DirectoryFilter filter, CancellationToken ct = default);

    Task<DirectoryListVm<Supplier>> SuppliersAsync(DirectoryFilter filter, CancellationToken ct = default);
    Task<byte[]> SuppliersCsvAsync(DirectoryFilter filter, CancellationToken ct = default);

    Task<DirectoryListVm<UptickUserListItemVm>> UptickUsersAsync(DirectoryFilter filter, CancellationToken ct = default);
    Task<byte[]> UptickUsersCsvAsync(DirectoryFilter filter, CancellationToken ct = default);
}

// The people and businesses imported from Uptick (contractors, suppliers, users), listed by name
public class DirectoryListService : IDirectoryListService
{
    private readonly AppDbContext _db;
    public DirectoryListService(AppDbContext db) => _db = db;

    private static async Task<DirectoryListVm<T>> PageAsync<T>(IQueryable<T> q, DirectoryFilter filter, CancellationToken ct)
    {
        var total = await q.CountAsync(ct);
        filter.Page = ListFilters.ClampPage(filter.Page, total);
        var items = await q.Skip((filter.Page - 1) * ListFilters.PageSize).Take(ListFilters.PageSize).ToListAsync(ct);
        return new DirectoryListVm<T> { Filter = filter, Items = items, Total = total };
    }

    private static string? Term(DirectoryFilter filter) =>
        string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim().ToLower();

    private static string? Date(DateTime? utc) => CsvExport.Date(utc.ToSydney());

    // ---- Contractors

    private IQueryable<Contractor> Contractors(DirectoryFilter filter)
    {
        var q = _db.Contractors.AsNoTracking();
        q = filter.Active switch { "inactive" => q.Where(c => !c.Active), "any" => q, _ => q.Where(c => c.Active) };
        if (Term(filter) is { } term)
            q = q.Where(c => c.Name.ToLower().Contains(term) ||
                             (c.Email != null && c.Email.ToLower().Contains(term)) ||
                             (c.PrimaryContact != null && c.PrimaryContact.ToLower().Contains(term)) ||
                             (c.Category != null && c.Category.ToLower().Contains(term)));
        return q.OrderByDescending(c => c.Starred).ThenBy(c => c.Name);
    }

    public Task<DirectoryListVm<Contractor>> ContractorsAsync(DirectoryFilter filter, CancellationToken ct = default) =>
        PageAsync(Contractors(filter), filter, ct);

    public async Task<byte[]> ContractorsCsvAsync(DirectoryFilter filter, CancellationToken ct = default) =>
        CsvExport.Build(
            new[] { "ID", "Name", "Category", "Primary contact", "Email", "Email CC", "Phone", "Mobile", "Address", "Skills", "Service areas", "Tags", "Starred", "Active" },
            (await Contractors(filter).ToListAsync(ct)).Select(c => new[]
            {
                c.ExternalId, c.Name, c.Category, c.PrimaryContact, c.Email, c.EmailCc, c.Phone, c.Mobile, c.Address,
                c.Skills, c.ServiceAreas, c.Tags, c.Starred ? "Yes" : "No", c.Active ? "Yes" : "No"
            }));

    // ---- Suppliers

    private IQueryable<Supplier> Suppliers(DirectoryFilter filter)
    {
        var q = _db.Suppliers.AsNoTracking();
        q = filter.Active switch { "inactive" => q.Where(s => !s.Active), "any" => q, _ => q.Where(s => s.Active) };
        if (Term(filter) is { } term)
            q = q.Where(s => s.Name.ToLower().Contains(term) ||
                             (s.Ref != null && s.Ref.ToLower().Contains(term)) ||
                             (s.BusinessNumber != null && s.BusinessNumber.Replace(" ", "").Contains(term.Replace(" ", ""))) ||
                             (s.AccountsEmail != null && s.AccountsEmail.ToLower().Contains(term)) ||
                             (s.PurchasingEmail != null && s.PurchasingEmail.ToLower().Contains(term)));
        return q.OrderBy(s => s.Name);
    }

    public Task<DirectoryListVm<Supplier>> SuppliersAsync(DirectoryFilter filter, CancellationToken ct = default) =>
        PageAsync(Suppliers(filter), filter, ct);

    public async Task<byte[]> SuppliersCsvAsync(DirectoryFilter filter, CancellationToken ct = default) =>
        CsvExport.Build(
            new[] { "ID", "Name", "Ref", "ABN", "Accounts contact", "Accounts email", "Purchasing contact", "Purchasing email", "Payment terms", "Accounting organisation", "Active" },
            (await Suppliers(filter).ToListAsync(ct)).Select(s => new[]
            {
                s.ExternalId, s.Name, s.Ref, s.BusinessNumber, s.AccountsContactName, s.AccountsEmail, s.PurchasingContactName,
                s.PurchasingEmail, s.PaymentTermsLabel, s.AccountingOrganisation, s.Active ? "Yes" : "No"
            }));

    // ---- Users: everyone in Uptick's Users export, with the FyreApp login linked to each
    // (the export only has a licence type, no active flag)

    private IQueryable<UptickUser> UptickUsers(DirectoryFilter filter)
    {
        var q = _db.UptickUsers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.License))
        {
            var license = filter.License.ToUpper();
            q = q.Where(u => u.LicenseType == license);
        }
        if (filter.Login == "yes")
            q = q.Where(u => _db.Users.Any(a => a.UptickUserId == u.Id));
        else if (filter.Login == "no")
            q = q.Where(u => !_db.Users.Any(a => a.UptickUserId == u.Id));
        if (Term(filter) is { } term)
            q = q.Where(u => u.Name.ToLower().Contains(term) ||
                             (u.Email != null && u.Email.ToLower().Contains(term)) ||
                             (u.Licence != null && u.Licence.ToLower().Contains(term)));
        // Staff first (desk, field), then contractors, then client portal users
        return q.OrderBy(u => u.LicenseType == "DESK" ? 0 : u.LicenseType == "FIELD" ? 1 : u.LicenseType == "CONTRACTOR" ? 2 : 3)
            .ThenBy(u => u.Name);
    }

    // The linked logins (with their role) by Uptick user id
    private async Task<Dictionary<int, LoginSummary>> LoginsAsync(IReadOnlyCollection<int>? uptickUserIds, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking().Where(a => a.UptickUserId != null);
        if (uptickUserIds != null) q = q.Where(a => uptickUserIds.Contains(a.UptickUserId!.Value));
        var rows = await q.Select(a => new
        {
            a.Id, UptickUserId = a.UptickUserId!.Value, a.IsActive,
            Role = (from ur in _db.UserRoles join r in _db.Roles on ur.RoleId equals r.Id where ur.UserId == a.Id select r.Name).FirstOrDefault()
        }).ToListAsync(ct);
        return rows.ToDictionary(r => r.UptickUserId, r => new LoginSummary(r.Id, r.Role, r.IsActive));
    }

    public async Task<DirectoryListVm<UptickUserListItemVm>> UptickUsersAsync(DirectoryFilter filter, CancellationToken ct = default)
    {
        var page = await PageAsync(UptickUsers(filter), filter, ct);
        var logins = await LoginsAsync(page.Items.Select(u => u.Id).ToList(), ct);
        var groupNames = await GroupNamesAsync(ct);
        return new DirectoryListVm<UptickUserListItemVm>
        {
            Filter = page.Filter,
            Total = page.Total,
            Items = page.Items.Select(u => new UptickUserListItemVm
            {
                User = u, Login = logins.GetValueOrDefault(u.Id),
                Groups = UptickReferenceValues.SplitSecurityGroups(u.SecurityGroups, groupNames)
            }).ToList()
        };
    }

    // Uptick's security group names (groups export), to split users' "Security Groups"
    private async Task<List<string>> GroupNamesAsync(CancellationToken ct) =>
        await _db.SecurityGroups.AsNoTracking().Select(g => g.Name).ToListAsync(ct);

    public async Task<byte[]> UptickUsersCsvAsync(DirectoryFilter filter, CancellationToken ct = default)
    {
        var logins = await LoginsAsync(null, ct);
        var groupNames = await GroupNamesAsync(ct);
        return CsvExport.Build(
            new[] { "ID", "Name", "Email", "Licence type", "Licence", "Security groups", "Service group", "Joined", "Last login", "FyreApp login" },
            (await UptickUsers(filter).ToListAsync(ct)).Select(u => new[]
            {
                u.ExternalId, u.Name, u.Email, UptickUser.LicenseLabel(u.LicenseType), u.Licence,
                string.Join("; ", UptickReferenceValues.SplitSecurityGroups(u.SecurityGroups, groupNames)), u.ServiceGroup,
                Date(u.JoinedUtc), Date(u.LastLoginUtc), logins.GetValueOrDefault(u.Id)?.Label ?? "No"
            }));
    }
}
