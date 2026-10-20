using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Lists;
using FyreApp.Services.Tasks;
using FyreApp.Tests.Helpers;
using Xunit;

namespace FyreApp.Tests.Services;

// Where Uptick's records show: a task's invoices and forms, products with their stock, a record's extra fields
public class UptickRecordPageTests
{
    [Fact]
    public async Task TaskPage_ShowsItsInvoices_AndFormsWithWhoSubmittedThem()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Ezfurn", Ref = "P-0391", Client = new Client { Name = "Ezfurn" } };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "PM2026/10", UptickId = 46502 };
        db.ClientTasks.Add(task);
        var wes = new UptickUser { UptickId = 305, Name = "Wes Speedon (Field Tech)", LicenseType = "FIELD" };
        db.UptickUsers.Add(wes);
        db.Invoices.AddRange(
            new Invoice { UptickId = 1, Number = "IV1", ClientTask = task, Total = 716.65m },
            new Invoice { UptickId = 2, Number = "IV2" });
        db.DynamicFormResponses.AddRange(
            new DynamicFormResponse { UptickId = 248, Ref = "F-00248", ClientTask = task, UptickUser = wes },
            new DynamicFormResponse { UptickId = 249, Ref = "F-00249", TargetContentType = "51", TargetObjectId = "46502" });
        await db.SaveChangesAsync();

        var vm = (await new TaskPageService(db).LoadAsync(task.Id, null))!;

        Assert.Equal("IV1", Assert.Single(vm.Invoices).Number);
        Assert.Equal(("F-00248", "Wes Speedon (Field Tech)"), Assert.Single(vm.Forms) is var f ? (f.Form.Ref, f.Submitter) : default);
    }

    [Fact]
    public async Task Products_ListWithTheirStock_ActiveByDefault()
    {
        using var db = DbContextFactory.Create();
        db.Products.AddRange(
            new Product { UptickId = 1, Name = "Exit sign", Code = "EXIT", Stocked = true },
            new Product { UptickId = 2, Name = "Old blanket", Active = false });
        db.Stocks.AddRange(
            new Stock { Warehouse = "Fyrepower Warehouse", Product = "Exit sign", OnHand = 4 },
            new Stock { Warehouse = "Dwayne Van", Product = "Exit sign", OnHand = 1 });
        db.Warehouses.Add(new Warehouse { UptickId = 1, Name = "Fyrepower Warehouse", Type = "FACILITY" });
        await db.SaveChangesAsync();
        var svc = new ProductListService(db);

        var active = await svc.SearchAsync(null, "yes", 1);
        var (product, stock) = Assert.Single(active.Items);
        Assert.Equal("Exit sign", product.Name);
        Assert.Equal(new[] { ("Dwayne Van", 1m), ("Fyrepower Warehouse", 4m) }, stock.Select(s => (s.Warehouse, s.OnHand ?? 0)));
        Assert.Single(active.Warehouses);

        Assert.Equal("Old blanket", Assert.Single((await svc.SearchAsync("blanket", "any", 1)).Items).Product.Name);
    }

}
