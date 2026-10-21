using FyreApp.Services.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FyreApp.Controllers;

// Uptick's products and stock (imported; a record only, edited in Uptick)
[Authorize(Roles = "Developer")]
public class ProductsController : Controller
{
    private readonly IProductListService _products;
    public ProductsController(IProductListService products) => _products = products;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string active = "yes", int page = 1, CancellationToken ct = default) =>
        View(await _products.SearchAsync(search, active, page, ct));
}
