using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Uptick.Mapping;

// Uptick's products -> Product (what quote, invoice and stock lines are for).
public class ProductMapper : UptickEntityMapper<Product>
{
    private Dictionary<long, string> _suppliers = new();

    public ProductMapper(AppDbContext db) : base(db) { }

    public override string Resource => "products";

    public override async Task BeginAsync(CancellationToken ct) =>
        _suppliers = await RoutineServiceTypeMapper.RawNamesAsync(Db, "suppliers", ct);

    protected override async Task<Dictionary<long, Product>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Products
            .Where(p => p.UptickId != null && ids.Contains(p.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, p => p.UptickId);
    }

    protected override Product Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Product p, UptickRaw raw)
    {
        p.UptickId = raw.Id;
        p.Name = raw.Text("name", 300) ?? $"Product {raw.Id}";
        p.Type = raw.Text("type", 20);
        p.Description = raw.Text("description");
        p.UnitDescription = raw.Text("unit_description", 300);
        p.Code = raw.Text("code", 100);
        p.Sku = raw.Text("sku", 100);
        p.Supplier = RoutineServiceTypeMapper.Cap(raw.Rel("supplier") is { } s ? _suppliers.GetValueOrDefault(s) : null, 300);
        p.Category = raw.Text("category", 200);
        p.Subcategory = raw.Text("subcategory", 200);
        p.CostPrice = raw.Decimal("cost_price");
        p.DefaultPrice = raw.Decimal("unit_price");
        p.Active = raw.Bool("is_active") ?? true;
        p.Stocked = raw.Bool("is_stocked") ?? false;
        p.Starred = raw.Bool("is_starred") ?? false;
    }

    protected override void Deactivate(Product p) => p.Active = false;
}

// Uptick's warehouses -> Warehouse: the warehouse or a technician's van, with its manager. Its stock value is the
// stock mapped into it (StockMapper).
public class WarehouseMapper : UptickEntityMapper<Warehouse>
{
    private Dictionary<long, string> _users = new();
    private Dictionary<long, string> _branches = new();

    public WarehouseMapper(AppDbContext db) : base(db) { }

    public override string Resource => "warehouses";

    public override async Task BeginAsync(CancellationToken ct)
    {
        _users = (await Db.UptickUsers.AsNoTracking().Where(u => u.UptickId != null)
                .Select(u => new { UptickId = u.UptickId!.Value, u.Name }).ToListAsync(ct))
            .ToDictionary(u => u.UptickId, u => u.Name);
        _branches = await RoutineServiceTypeMapper.RawNamesAsync(Db, "branches", ct);
    }

    protected override async Task<Dictionary<long, Warehouse>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        var rows = await Db.Warehouses
            .Where(w => w.UptickId != null && ids.Contains(w.UptickId.Value))
            .ToListAsync(ct);
        return Match(rows, w => w.UptickId);
    }

    protected override Warehouse Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Warehouse w, UptickRaw raw)
    {
        w.UptickId = raw.Id;
        w.Name = raw.Text("name", 200) ?? $"Warehouse {raw.Id}";
        w.Type = raw.Text("type", 30);
        w.Branch = RoutineServiceTypeMapper.Cap(raw.Rel("branch") is { } b ? _branches.GetValueOrDefault(b) : null, 200) ?? w.Branch;
        w.Manager = RoutineServiceTypeMapper.Cap(raw.Rel("manager") is { } m ? _users.GetValueOrDefault(m) : null, 200);
    }

    // A warehouse gone from Uptick holds no stock there any more
    protected override void Deactivate(Warehouse w) => w.StockValue = 0;
}

// Uptick's stock -> Stock: how much of a product a warehouse holds. These replace the annotated stocks export's
// snapshot, whose rows (no ids) are cleared on the first pass.
public class StockMapper : UptickEntityMapper<Stock>
{
    private Dictionary<long, Product> _products = new();
    private Dictionary<long, (int Id, string Name)> _warehouses = new();

    public StockMapper(AppDbContext db) : base(db) { }

    public override string Resource => "stock";

    public override async Task BeginAsync(CancellationToken ct)
    {
        var snapshot = await Db.Stocks.Where(s => s.UptickId == null).ToListAsync(ct);
        if (snapshot.Count > 0)
        {
            Db.Stocks.RemoveRange(snapshot);
            await Db.SaveChangesAsync(ct);
        }

        _products = await Db.Products.AsNoTracking().Where(p => p.UptickId != null).ToDictionaryAsync(p => p.UptickId!.Value, ct);
        _warehouses = (await Db.Warehouses.AsNoTracking().Where(w => w.UptickId != null)
                .Select(w => new { UptickId = w.UptickId!.Value, w.Id, w.Name }).ToListAsync(ct))
            .ToDictionary(w => w.UptickId, w => (w.Id, w.Name));
    }

    protected override async Task<Dictionary<long, Stock>> ExistingAsync(IReadOnlyList<UptickRaw> batch, CancellationToken ct)
    {
        var ids = batch.Select(r => r.Id).ToList();
        return await Db.Stocks.Where(s => s.UptickId != null && ids.Contains(s.UptickId.Value)).ToDictionaryAsync(s => s.UptickId!.Value, ct);
    }

    protected override Stock Create(UptickRaw raw) => new() { UptickId = raw.Id };

    protected override void Map(Stock s, UptickRaw raw)
    {
        if (raw.Rel("product") is not { } productId || !_products.TryGetValue(productId, out var product))
            throw new UptickMapException($"Its product ({raw.Rel("product")}) isn't in FyreApp.");
        if (raw.Rel("warehouse") is not { } warehouseId || !_warehouses.TryGetValue(warehouseId, out var warehouse))
            throw new UptickMapException($"Its warehouse ({raw.Rel("warehouse")}) isn't in FyreApp.");

        var onHand = raw.Decimal("amount_on_hand");
        var reserved = raw.Decimal("amount_reserved_outgoing");
        var restock = raw.Decimal("minimum_restock_level");
        var available = onHand - (reserved ?? 0);

        s.UptickId = raw.Id;
        s.ProductId = product.Id;
        s.Product = product.Name;
        s.WarehouseId = warehouse.Id;
        s.Warehouse = warehouse.Name;
        s.SupplierRef = product.Sku;
        s.InternalRef = product.Code;
        s.Category = product.Category;
        s.Supplier = product.Supplier;
        s.OnHand = onHand;
        s.Reserved = reserved;
        s.OnOrder = raw.Decimal("amount_reserved_incoming");
        s.Available = available;
        s.RestockLevel = restock;
        s.Required = restock is > 0 && available < restock ? restock - available : 0;
        s.BinLocation = raw.Text("bin_location", 100);
        s.StockValue = onHand * product.CostPrice;
    }

    protected override void Deactivate(Stock s) => Db.Stocks.Remove(s);

    // Each warehouse's stock value is what it holds
    protected override async Task AfterAsync(CancellationToken ct)
    {
        var values = await Db.Stocks.Where(s => s.WarehouseId != null)
            .GroupBy(s => s.WarehouseId!.Value)
            .Select(g => new { Id = g.Key, Value = g.Sum(s => s.StockValue ?? 0) })
            .ToDictionaryAsync(g => g.Id, g => g.Value, ct);
        foreach (var w in await Db.Warehouses.Where(w => w.UptickId != null).ToListAsync(ct))
            w.StockValue = values.GetValueOrDefault(w.Id);
        await Db.SaveChangesAsync(ct);
    }
}
