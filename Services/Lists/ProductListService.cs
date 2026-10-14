using FyreApp.Data;
using FyreApp.Models;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Lists;

public interface IProductListService
{
    Task<ProductListVm> SearchAsync(string? search, string active, int page, CancellationToken ct = default);
}

// Products page: Uptick's products, with the stock each warehouse holds (products, warehouses and
// annotated stocks exports; stock is matched to a product by name)
public class ProductListService : IProductListService
{
    private readonly AppDbContext _db;
    public ProductListService(AppDbContext db) => _db = db;

    public async Task<ProductListVm> SearchAsync(string? search, string active, int page, CancellationToken ct = default)
    {
        var q = _db.Products.AsNoTracking();
        active = active is "no" or "any" ? active : "yes";
        q = active switch { "no" => q.Where(p => !p.Active), "any" => q, _ => q.Where(p => p.Active) };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(term) ||
                             (p.Code != null && p.Code.ToLower().Contains(term)) ||
                             (p.Category != null && p.Category.ToLower().Contains(term)) ||
                             (p.Supplier != null && p.Supplier.ToLower().Contains(term)));
        }
        q = q.OrderByDescending(p => p.Starred).ThenBy(p => p.Name);

        var total = await q.CountAsync(ct);
        page = ListFilters.ClampPage(page, total);
        var products = await q.Skip((page - 1) * ListFilters.PageSize).Take(ListFilters.PageSize).ToListAsync(ct);

        var names = products.Select(p => p.Name).ToList();
        var stock = (await _db.StockLevels.AsNoTracking().Where(s => names.Contains(s.Product)).ToListAsync(ct))
            .ToLookup(s => s.Product);

        return new ProductListVm
        {
            Search = search, Active = active, Page = page, Total = total,
            Items = products.Select(p => (p, (IReadOnlyList<StockLevel>)stock[p.Name].OrderBy(s => s.Warehouse).ToList())).ToList(),
            Warehouses = await _db.Warehouses.AsNoTracking().OrderBy(w => w.Type).ThenBy(w => w.Name).ToListAsync(ct)
        };
    }
}
