using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Services.Lists;
using FyreApp.Services.Tasks;
using FyreApp.Tests.Helpers;
using FyreApp.ViewModels.Lists;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Uptick's billing, quotes, schedules, forms, accreditations, products and stock exports, and where they show
public class UptickBillingImportTests
{
    private static Stream Csv(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    // Every Uptick importer, export type left to detection, as when an admin just drops the file in
    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, Stream csv) =>
        new UptickImportService(new UptickImporter[]
        {
            new PropertyImporter(db), new PropertyContactImporter(db), new AssetImporter(db), new RemarkImporter(db), new ReportImporter(db),
            new TaskImporter(db), new AssetTypeImporter(db), new AssetTypeVariantImporter(db), new RemarkTypeImporter(db),
            new RoutineServiceTypeImporter(db), new RoutineServiceLevelImporter(db), new ClientContactImporter(db),
            new UserImporter(db), new ContractorImporter(db), new SupplierImporter(db),
            new SecurityGroupImporter(db), new ExtraFieldImporter(db), new PromptQuestionImporter(db),
            new ProductImporter(db), new WarehouseImporter(db), new StockLevelImporter(db), new AccreditationImporter(db),
            new BillingCardImporter(db), new BillingContractImporter(db), new BillingContractLineItemImporter(db), new InvoiceImporter(db),
            new CreditNoteImporter(db), new DefectQuoteImporter(db), new SalesQuoteImporter(db), new PropertyScheduleImporter(db),
            new FormResponseImporter(db)
        }).ImportAsync(csv, "export.csv", null, dryRun: false);

    // Header rows as Uptick writes them (14 Oct 2026 exports)
    private const string InvoiceHeader = "\"ID\",\"Number\",\"Invoice Ref\",\"Created\",\"Updated\",\"Author\",\"Description\",\"Property Ref\",\"Property\",\"Branch\",\"Client Ref\",\"Client\",\"Billing Card Ref\",\"Billing Card Name\",\"Date\",\"Due Date\",\"Days Overdue\",\"Last Reminder Sent At\",\"Subtotal\",\"Tax\",\"Total\",\"Credit Total\",\"Status\",\"Task Ref\",\"Task Name\",\"Task ID\",\"Task Status\",\"Task Cost Centre\",\"Task Category\",\"Task Service Group\",\"Task Technician Note\"";
    private const string StockHeader = "\"Warehouse\",\"Product\",\"Supplier Ref\",\"Internal Ref\",\"Category\",\"Supplier\",\"On Hand\",\"Available\",\"Reserved\",\"On Order\",\"Restock Level\",\"Required\",\"Bin Location\",\"Stock Value\"";

    public static TheoryData<string, UptickExportType> Headers => new()
    {
        { "\"ID\",\"Created\",\"Updated\",\"Expiry\",\"Property Name\",\"Property Ref\",\"Type\",\"Technician\",\"Number\"", UptickExportType.Accreditations },
        { StockHeader, UptickExportType.StockLevels },
        { "\"ID\",\"Created\",\"Updated\",\"Synced\",\"Name\",\"Ref\",\"Business Number\",\"Postal Attention\",\"Postal Email\",\"Postal Mobile\",\"Postal Business Hours\",\"Postal After Hours\",\"Postal Address\",\"Number of Invoices\",\"Attached Clients\",\"Attached Properties\",\"Partner UID\",\"Accounting Organisation\",\"Default Tax Code\",\"Credit Limit\",\"Minimum Days Overdue\"", UptickExportType.BillingCards },
        { "\"ID\",\"Billing Contract Name\",\"Billing Contract ID\",\"Billing Contract Type\",\"Routine Type\",\"Level Type\",\"All Routines\",\"Product\",\"Description\",\"Unit Price\",\"Type\",\"Price Period\",\"Visible\",\"Property Ref\",\"Property\",\"Client Name\"", UptickExportType.BillingContractLineItems },
        { "\"ID\",\"Name\",\"Active\",\"Created\",\"Updated\",\"Type\",\"Frequency\",\"Invoice mode\",\"Property\",\"Property ref\",\"Client\",\"Start date\",\"Finish date\",\"Next due\",\"Annual value\",\"Notes\",\"Price increase policy\"", UptickExportType.BillingContracts },
        { "id,number,status,date,description,partner_uid,invoice_id,invoice__number,invoice__client__name,invoice__client__ref,invoice__property__name,invoice__property__ref", UptickExportType.CreditNotes },
        { "\"id\",\"ref\",\"submitted\",\"submitter\",\"template_version\",\"values\",\"target_contenttype\",\"target_contenttype__model\",\"related_action\",\"target_object_id\",\"is_complete\"", UptickExportType.FormResponses },
        { InvoiceHeader, UptickExportType.Invoices },
        { "\"ID\",\"Created\",\"Updated\",\"Supplier\",\"Name\",\"Type\",\"Description\",\"Unit Description\",\"Code\",\"SKU\",\"UPC\",\"Starred\",\"Active\",\"Stocked\",\"Cost Price\",\"Default Price\",\"Category\",\"Subcategory\",\"Last Used\"", UptickExportType.Products },
        { "\"ID\",\"Reference\",\"Created\",\"Status\",\"Property\",\"Property Ref\",\"Client\",\"Date\",\"Due Date\",\"Expiry Date\",\"Description\",\"Subtotal\",\"Tax\",\"Total\",\"Total material cost\",\"Total labour cost\",\"Scope of Works\"", UptickExportType.DefectQuotes },
        { "\"ID\",\"Ref\",\"Status\",\"Property\",\"Address\",\"Client\",\"Description\",\"Scope of Works\",\"Created\",\"Expiry date\",\"Template\",\"Work and services subtotal\",\"Programme maintenance subtotal sell\"", UptickExportType.SalesQuotes },
        { "\"Property Id\",\"Property Name\",\"Property Ref\",\"Schedule Number\",\"Schedule Date\",\"Schedule Type\",\"Issuer\"", UptickExportType.PropertySchedules },
        { "\"ID\",\"Created\",\"Updated\",\"Type\",\"Name\",\"Branch\",\"Manager\",\"Required\",\"Reserved (Incoming Stock)\",\"Reserved (Outgoing Stock)\",\"Stock Value\"", UptickExportType.Warehouses }
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public async Task EachExport_IsRecognisedFromItsHeaderRow_EvenWithNoRows(string header, UptickExportType expected)
    {
        using var db = DbContextFactory.Create();

        var result = await Import(db, Csv(header));

        Assert.Equal(((UptickExportType?)expected, (string?)null), (result.Type, result.Error));
    }

    [Fact]
    public async Task Invoices_Import_AndRefreshOnRerun()
    {
        using var db = DbContextFactory.Create();
        string Row(string status) =>
            $"52832,\"IV000032823\",\"P-0391\",\"2026-10-14 12:23:34\",\"2026-10-14 12:25:11\",\"FyrePower Team\",\"P-0391 Ezfurn\",\"P-0391\",\"Ezfurn\",\"\",\"\",\"Ezfurn\",\"\",\"Ez furn\",\"2026-10-14\",\"2026-10-21\",\"\",\"\",\"1750.0000\",\"175.0000\",\"1925.0000\",\"0.00\",\"{status}\",\"T-46419\",\"PM2026/10 Servicing\",\"46419\",\"COMPLETE\",\"\",\"I&T\",\"\",\"\"";

        var first = await Import(db, Csv(InvoiceHeader, Row("AUTHORISED")));
        var invoice = await db.Invoices.SingleAsync();
        Assert.Equal(1, first.Created);
        Assert.Equal(("IV000032823", "P-0391", "46419", "T-46419", 1925.00m, "AUTHORISED", new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)),
            (invoice.Number, invoice.PropertyRef, invoice.TaskExternalId, invoice.TaskRef, invoice.Total, invoice.Status, invoice.DueDate));

        var second = await Import(db, Csv(InvoiceHeader, Row("PAID")));
        Assert.Equal((0, 1), (second.Created, second.Updated));
        Assert.Equal("PAID", (await db.Invoices.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task CreditNotes_ReadUpticksLowerCaseColumns()
    {
        using var db = DbContextFactory.Create();

        await Import(db, Csv("id,number,status,date,description,partner_uid,invoice_id,invoice__number,invoice__client__name,invoice__client__ref,invoice__property__name,invoice__property__ref",
            "1,\"CN-31040\",\"CREDIT\",\"2026-05-06\",\"\",\"d533\",30761,\"IV000030454\",\"Park Cove\",\"\",\"Marina at Park Cove\",\"P-2614\""));

        var note = await db.CreditNotes.SingleAsync();
        Assert.Equal(("CN-31040", "30761", "IV000030454", "P-2614"), (note.Number, note.InvoiceExternalId, note.InvoiceNumber, note.PropertyRef));
    }

    [Fact]
    public async Task Stock_HasNoIds_SoEachImportReplacesIt()
    {
        using var db = DbContextFactory.Create();

        await Import(db, Csv(StockHeader,
            "\"Dwayne Van\",\"9L Air & Water Extinguisher (NEW)\",\"\",\"9LAW\",\"\",\"\",\"-1.00\",\"-1.00\",\"0\",\"0\",\"0\",\"1\",\"\",\"0\"",
            "\"Fyrepower Warehouse\",\"Exit sign\",\"\",\"\",\"\",\"\",\"4\",\"4\",\"0\",\"0\",\"2\",\"0\",\"A1\",\"120\""));
        var second = await Import(db, Csv(StockHeader,
            "\"Fyrepower Warehouse\",\"Exit sign\",\"\",\"\",\"\",\"\",\"3\",\"3\",\"0\",\"0\",\"2\",\"0\",\"A1\",\"90\""));

        Assert.Contains(second.Notes, n => n.Contains("2 stock lines already in FyreApp were replaced"));
        var line = await db.StockLevels.SingleAsync();
        Assert.Equal(("Exit sign", 3m, "A1"), (line.Product, line.OnHand, line.BinLocation));
    }

    [Fact]
    public async Task TaskPage_ShowsItsInvoices_AndFormsWithWhoSubmittedThem()
    {
        using var db = DbContextFactory.Create();
        var site = new Property { Name = "Ezfurn", ExternalId = "P-0391", Client = new Client { Name = "Ezfurn" } };
        var task = new ClientTask { Property = site, Client = site.Client, Title = "PM2026/10", ExternalId = "46502" };
        db.ClientTasks.Add(task);
        db.UptickUsers.Add(new UptickUser { ExternalId = "305", Name = "Wes Speedon (Field Tech)", LicenseType = "FIELD" });
        db.Invoices.AddRange(
            new Invoice { ExternalId = "1", Number = "IV1", TaskExternalId = "46502", Total = 716.65m },
            new Invoice { ExternalId = "2", Number = "IV2", TaskExternalId = "99999" });
        db.FormResponses.AddRange(
            new FormResponse { ExternalId = "248", Ref = "F-00248", TargetContentType = FormResponse.TaskContentType, TargetObjectId = "46502", SubmitterExternalId = "305" },
            new FormResponse { ExternalId = "249", Ref = "F-00249", TargetContentType = "51", TargetObjectId = "46502" });
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
            new Product { ExternalId = "1", Name = "Exit sign", Code = "EXIT", Stocked = true },
            new Product { ExternalId = "2", Name = "Old blanket", Active = false });
        db.StockLevels.AddRange(
            new StockLevel { Warehouse = "Fyrepower Warehouse", Product = "Exit sign", OnHand = 4 },
            new StockLevel { Warehouse = "Dwayne Van", Product = "Exit sign", OnHand = 1 });
        db.Warehouses.Add(new Warehouse { ExternalId = "1", Name = "Fyrepower Warehouse", Type = "FACILITY" });
        await db.SaveChangesAsync();
        var svc = new ProductListService(db);

        var active = await svc.SearchAsync(null, "yes", 1);
        var (product, stock) = Assert.Single(active.Items);
        Assert.Equal("Exit sign", product.Name);
        Assert.Equal(new[] { ("Dwayne Van", 1m), ("Fyrepower Warehouse", 4m) }, stock.Select(s => (s.Warehouse, s.OnHand ?? 0)));
        Assert.Single(active.Warehouses);

        Assert.Equal("Old blanket", Assert.Single((await svc.SearchAsync("blanket", "any", 1)).Items).Product.Name);
    }

    [Fact]
    public async Task UsersList_ShowsTechniciansAccreditations()
    {
        using var db = DbContextFactory.Create();
        db.UptickUsers.Add(new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD" });
        db.Accreditations.Add(new Accreditation { ExternalId = "67", Type = "Licence No.:", Number = "QBCC 15366855 | FPA 25023", Technician = "Dwayne Page" });
        await db.SaveChangesAsync();

        var list = await new DirectoryListService(db).UptickUsersAsync(new DirectoryFilter { Active = "any" });

        Assert.Equal("QBCC 15366855 | FPA 25023", Assert.Single(Assert.Single(list.Items).Accreditations).Number);
    }
}
