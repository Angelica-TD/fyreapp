using FyreApp.Models;

namespace FyreApp.ViewModels.Lists;

// Products page: search, active (default active), a page of products with their stock, and the warehouses
public class ProductListVm
{
    public string? Search { get; set; }
    public string Active { get; set; } = "yes";
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int Pages => ListFilters.Pages(Total);

    public IReadOnlyList<(Product Product, IReadOnlyList<StockLevel> Stock)> Items { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
}
