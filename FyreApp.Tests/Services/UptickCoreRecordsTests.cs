using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Uptick.Mapping;
using FyreApp.Services.Uptick.Migration;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FyreApp.Tests.Services;

// Uptick's clients, properties, their contacts and users taken into FyreApp's tables (docs/uptick-migration.md
// phases 3b and 3c, with asset types, variants and assets): linked by Uptick id, rows from earlier file imports updated in place, deletions deactivating
public class UptickCoreRecordsTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    private static UptickRecord Raw(string resource, string id, string attributes, string relationships = "{}") => new()
    {
        Resource = resource, UptickId = id, Attributes = attributes, Relationships = relationships, Hash = id,
        FetchedUtc = Now, ChangedUtc = Now, SeenUtc = Now
    };

    private static string To(string type, string? id) =>
        id == null ? """{"data":null}""" : "{\"data\":{\"id\":\"" + id + "\",\"type\":\"" + type + "\"}}";

    private static UptickRecord RawClient(string id, string name, bool active = true) => Raw("clients", id,
        "{\"name\":\"" + name + "\",\"ref\":\"\",\"sector\":\"Retail Trade\",\"is_active\":" + (active ? "true" : "false") +
        ",\"contact_name\":\"Belinda Boyd\",\"contact_email\":\"anita@alliedx.com.au\",\"contact_phone_bh\":\"\"," +
        "\"created\":\"2026-10-15T11:37:44.374802+11:00\",\"updated\":\"2026-10-15T11:37:44.374973+11:00\"}",
        "{\"pricetier\":" + To("PriceTier", "0") + "}");

    private static UptickRecord RawProperty(string id, string reference, string clientId, string status = "ACTIVE") => Raw("properties", id,
        "{\"ref\":\"" + reference + "\",\"name\":\"Big River Trade Centre\",\"status\":\"" + status + "\"," +
        "\"coords\":{\"lat\":-28.1045527,\"lng\":153.4129512}," +
        "\"address\":{\"city\":\"Burleigh Heads\",\"state\":\"QLD\",\"display\":\"11 Greg Chappell Dr, Burleigh Heads QLD 4220, Australia\"," +
        "\"place_id\":\"ChIJz3rp\",\"streetline\":\"11 Greg Chappell Dr\",\"postal_code\":\"4220\"}," +
        "\"created\":\"2021-03-12T12:22:48.025837+11:00\",\"inactive_date\":null,\"access_schedule\":\"BH\",\"access_procedure\":\"CH\"," +
        "\"access_code\":\"\",\"access_note\":\"Contact Nathan on 0436 917 675\",\"internal_note\":\"\"}",
        "{\"client\":" + To("Client", clientId) + ",\"billingcard\":" + To("BillingCard", "157") + "}");

    private static UptickRecord RawPropertyContact(string id, string propertyId) => Raw("propertycontacts", id,
        """{"name":"Jared Bourke","role":"owner","email":"jbourke@andersens.com.au","mobile":"0487181429","phone_bh":"","organisation":"","requires_quote":false,"requires_report":true,"requires_invoice":false,"requires_notification":false,"is_active":true}""",
        "{\"property\":" + To("Property", propertyId) + "}");

    private static UptickRecord RawClientContact(string id, string clientId) => Raw("clientcontacts", id,
        """{"name":"Christian","email":"","phone":"","mobile":"0434 043 048","position":"Knight Frank Officer","is_active":true}""",
        "{\"client\":" + To("Client", clientId) + "}");

    private static UptickRecord RawUser(string id, string name, string email, string license, string? serviceGroup = null) => Raw("users", id,
        "{\"name\":\"" + name + "\",\"email\":\"" + email + "\",\"license\":\"" + license + "\",\"rate\":\"55.00\",\"sell_rate\":null," +
        "\"date_joined\":\"2021-02-05T17:20:54.739990+11:00\",\"last_login\":null,\"extra_fields\":{\"license\":\"QBCC: 117726\"}}",
        "{\"home_branch\":{\"data\":null},\"servicegroup\":" + To("ServiceGroup", serviceGroup) + "}");

    private static UptickMapping Mapping(AppDbContext db, bool keepEdits = false) => new(db,
        [new BillingCardMapper(db), new ClientMapper(db), new PropertyMapper(db), new ClientContactMapper(db), new PropertyContactMapper(db), new UserMapper(db),
            new SecurityGroupMapper(db), new ContractorMapper(db), new SupplierMapper(db), new AccreditationMapper(db),
            new ExtraFieldMapper(db), new PromptQuestionMapper(db), new PropertyScheduleMapper(db),
            new AssetTypeMapper(db), new AssetTypeVariantMapper(db), new AssetMapper(db),
            new RoutineServiceTypeMapper(db), new RoutineServiceLevelMapper(db), new RemarkTypeMapper(db), new AssetTypeRoutineLinksMapper(db),
            new ProductMapper(db), new WarehouseMapper(db), new StockMapper(db), new BillingContractMapper(db), new BillingContractLineItemMapper(db), new RoutineServiceMapper(db), new RoutineServiceLevelLinkMapper(db), new TaskMapper(db), new ServiceTaskMapper(db),
            new DefectQuoteMapper(db), new ServiceQuoteMapper(db), new InvoiceMapper(db), new CreditNoteMapper(db), new DynamicFormResponseMapper(db), new RemarkMapper(db),
            new ReportMapper(db), new ReportItemMapper(db), new RoutineMapper(db),
            new SubtaskMapper(db), new TaskSessionMapper(db), new AppointmentMapper(db), new AppointmentTechniciansMapper(db), new TaskStockTransactionMapper(db),
            new CorrespondenceMapper(db), new CorrespondenceLinkMapper(db), new DispatchMapper(db),
            new DefectQuoteLineMapper(db), new InvoiceLineMapper(db), new ServiceQuoteProductLineMapper(db), new ServiceQuoteFixedLineMapper(db),
            new ServiceQuoteDoAndChargeLineMapper(db), new RemarkEventMapper(db),
            new TimelineEventMapper(db)],
        NullLogger<UptickMapping>.Instance, null, Microsoft.Extensions.Options.Options.Create(new UptickMappingOptions { KeepFyreAppEdits = keepEdits }));

    // Every mapper in order, as a run ends
    private static async Task<Dictionary<string, UptickEndpointResult>> MapAllAsync(AppDbContext db, bool remap = false, bool keepEdits = false)
    {
        var mapping = Mapping(db, keepEdits);
        var results = new Dictionary<string, UptickEndpointResult>();
        foreach (var resource in mapping.Resources)
        {
            var result = new UptickEndpointResult { Resource = resource };
            await mapping.MapAsync(resource, remap, result, null, CancellationToken.None);
            Assert.Null(result.MapError);
            results[resource] = result;
        }
        return results;
    }

    [Fact]
    public async Task Clients_properties_and_contacts_are_mapped_and_linked_by_Uptick_id()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied OT Pty Limited"), RawProperty("1491", "P-0016", "2806"),
            RawPropertyContact("5780", "1491"), RawClientContact("3", "2806"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal(1, results["properties"].Mapped);

        var client = await db.Clients.SingleAsync();
        Assert.Equal((2806L, "2806", "Allied OT Pty Limited", true), (client.UptickId!.Value, client.DisplayRef, client.Name, client.Active));
        Assert.Equal(("Belinda Boyd", "anita@alliedx.com.au", null, "Retail Trade"),
            (client.PrimaryContactName, client.PrimaryContactEmail, client.PrimaryContactPhone, client.PrimarySector));
        Assert.Equal(DateTimeOffset.Parse("2026-10-15T11:37:44.374802+11:00").UtcDateTime, client.Created);

        var property = await db.Properties.SingleAsync();
        Assert.Equal((1491L, "P-0016", "P-0016", client.Id), (property.UptickId!.Value, property.Ref, property.DisplayRef, property.ClientId));
        Assert.Equal("11 Greg Chappell Dr, Burleigh Heads QLD 4220", property.AddressDisplay);
        Assert.Equal(("11 Greg Chappell Dr", "Burleigh Heads", "QLD", "4220", "ChIJz3rp"),
            (property.AddressLine1, property.Suburb, property.State, property.Postcode, property.GooglePlaceId));
        Assert.Equal((-28.1045527, 153.4129512), (property.Latitude!.Value, property.Longitude!.Value));
        Assert.Equal(("ACTIVE", true, "BH", null), (property.Status, property.Active, property.AccessSchedule, property.AccessCode));

        var contact = await db.PropertyContacts.SingleAsync();
        Assert.Equal((5780L, property.Id, "Jared Bourke", "owner", true, false), (contact.UptickId!.Value, contact.PropertyId, contact.Name,
            contact.Role, contact.ReportRequired, contact.InvoiceRequired));

        var clientContact = await db.ClientContacts.SingleAsync();
        Assert.Equal((3L, client.Id, "Christian", null, "Knight Frank Officer"),
            (clientContact.UptickId!.Value, clientContact.ClientId, clientContact.Name, clientContact.Email, clientContact.Position));
    }

    [Fact]
    public async Task Rows_FyreApp_holds_are_updated_in_place_and_placeholders_filled_in()
    {
        await using var db = DbContextFactory.Create();
        var placeholder = new Client { Name = "Client 2806", UptickId = 2806, IsPlaceholder = true, Active = false, ReportAddress = "PO Box 1" };
        db.Clients.Add(placeholder);
        db.Properties.Add(new Property { Name = "Old name", UptickId = 1491, Ref = "P-0016", Client = placeholder });
        db.UptickUsers.Add(new UptickUser { Name = "Wes", UptickId = 12, SecurityGroups = "Technician" });
        db.UptickRecords.AddRange(RawClient("2806", "Allied OT Pty Limited"), RawProperty("1491", "P-0016", "2806"),
            RawUser("12", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var client = await db.Clients.SingleAsync();
        Assert.Equal((placeholder.Id, 2806L, "Allied OT Pty Limited", false, true),
            (client.Id, client.UptickId!.Value, client.Name, client.IsPlaceholder, client.Active));
        // The API has no report address; the export's is kept
        Assert.Equal("PO Box 1", client.ReportAddress);

        var property = await db.Properties.SingleAsync();
        Assert.Equal((1491L, "Big River Trade Centre"), (property.UptickId!.Value, property.Name));

        var user = await db.UptickUsers.SingleAsync();
        Assert.Equal((12L, "Wes Speedon (Field Tech)", "FIELD", "Technician", 55.00m, "QBCC: 117726"),
            (user.UptickId!.Value, user.Name, user.LicenseType, user.SecurityGroups, user.CostRate!.Value, user.Licence));
    }

    [Fact]
    public async Task A_property_whose_client_isnt_held_waits_and_is_mapped_once_it_is()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawProperty("1491", "P-0016", "2806"), RawPropertyContact("5780", "1491"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal((0, 1), (results["properties"].Mapped!.Value, results["properties"].MapFailed));
        Assert.Contains("1491: Its client (2806) isn't in FyreApp.", results["properties"].MapErrors);
        Assert.Equal(1, results["propertycontacts"].MapFailed);
        Assert.Empty(await db.Properties.ToListAsync());

        // The client arrives with the next run; what waited is mapped then
        db.UptickRecords.Add(RawClient("2806", "Allied OT Pty Limited"));
        await db.SaveChangesAsync();
        results = await MapAllAsync(db);
        Assert.Equal((1, 1), (results["properties"].Mapped!.Value, results["propertycontacts"].Mapped!.Value));
        Assert.Equal(1491L, (await db.Properties.Include(p => p.Contacts).SingleAsync()).Contacts.Single().Property.UptickId);
    }

    [Fact]
    public async Task Records_deleted_in_Uptick_deactivate_their_rows_and_a_remap_keeps_ids()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied OT Pty Limited"), RawProperty("1491", "P-0016", "2806"),
            RawPropertyContact("5780", "1491"));
        await db.SaveChangesAsync();
        await MapAllAsync(db);
        var ids = (await db.Clients.SingleAsync()).Id;

        foreach (var r in await db.UptickRecords.ToListAsync())
        {
            r.DeletedUtc = Now;
            r.MappedUtc = null;
        }
        await db.SaveChangesAsync();
        await MapAllAsync(db);

        Assert.False((await db.Clients.SingleAsync()).Active);
        Assert.False((await db.Properties.SingleAsync()).Active);
        Assert.False((await db.PropertyContacts.SingleAsync()).Active);

        // Back in Uptick, then mapped again from scratch: the same rows, active again
        foreach (var r in await db.UptickRecords.ToListAsync()) r.DeletedUtc = null;
        await db.SaveChangesAsync();
        await MapAllAsync(db, remap: true);
        var client = await db.Clients.SingleAsync();
        Assert.Equal((ids, true), (client.Id, client.Active));
        Assert.True((await db.Properties.SingleAsync()).Active);
    }

    [Fact]
    public async Task Users_link_logins_by_email_but_never_to_an_API_login()
    {
        await using var db = DbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { UserName = "wes", Email = "wes@fyrepower.com.au", FirstName = "W", LastName = "S" },
            new ApplicationUser { UserName = "admin", Email = "admin@example.com", FirstName = "Ad", LastName = "Min" });
        db.UptickRecords.AddRange(Raw("servicegroups", "4", """{"name":"Servicing - Portables & Fire Equipment"}"""),
            RawUser("12", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD", serviceGroup: "4"),
            RawUser("437", "FyreApp Integration", "admin@example.com", "API"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var wes = await db.UptickUsers.SingleAsync(u => u.UptickId == 12);
        Assert.Equal("Servicing - Portables & Fire Equipment", wes.ServiceGroup);
        Assert.Equal(wes.Id, (await db.Users.SingleAsync(u => u.UserName == "wes")).UptickUserId);

        var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
        Assert.Null(admin.UptickUserId);
        Assert.Equal("Ad", admin.FirstName);
    }

    [Fact]
    public async Task Timeline_events_reach_a_property_mapped_from_the_API()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied OT Pty Limited"), RawProperty("1491", "P-0016", "2806"),
            Raw("contenttypes", "19", """{"model":"property"}"""),
            Raw("timelineevents", "1", """{"key":"general","type":"E","text":"Property created.","object_id":1491}""",
                "{\"content_type\":" + To("ContentType", "19") + "}"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        Assert.Equal((await db.Properties.SingleAsync()).Id, (await db.TimelineEvents.SingleAsync()).PropertyId);
    }

    private static UptickRecord RawAssetType(string id, string name) => Raw("assettypes", id,
        "{\"name\":\"" + name + "\",\"category\":\"AS1851 Sec 10 - Portable Fire Extinguishers\",\"sequence_group\":\"Portables\",\"description\":\"\",\"is_active\":true}");

    private static UptickRecord RawVariant(string id, string typeId, string name, string? productId = null) => Raw("assettypevariants", id,
        "{\"name\":\"" + name + "\",\"is_active\":true}",
        "{\"type\":" + To("AssetType", typeId) + ",\"default_replacement_product\":" + To("Product", productId) + "}");

    private static UptickRecord RawAsset(string id, string propertyId, string typeId, string? variantId = null, string? servicedBy = null,
        string result = "P", string label = "") => Raw("assets", id,
        "{\"ref\":\"001\",\"label\":\"" + label + "\",\"location\":\"Kitchen\",\"make\":\"\",\"barcode\":\"\",\"quantity\":2," +
        "\"inspection_ref\":\"\",\"compliant\":" + (result == "F" ? "false" : "true") + ",\"last_service_result\":\"" + result + "\"," +
        "\"is_active\":true,\"base_date\":null,\"installation_date\":\"2019-06-01\",\"inspected_date\":\"2026-10-15\"," +
        "\"inactive_date\":null,\"internal_note\":\"\",\"inspection_order\":3}",
        "{\"type\":" + To("AssetType", typeId) + ",\"variant\":" + To("AssetTypeVariant", variantId) + ",\"property\":" + To("Property", propertyId) +
        ",\"serviced_by\":" + To("User", servicedBy) + "}");

    [Fact]
    public async Task Assets_are_mapped_with_their_type_variant_and_who_last_serviced_them()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied OT Pty Limited"), RawProperty("1491", "P-0016", "2806"),
            RawUser("371", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"),
            Raw("products", "905", """{"name":"5.0X C02 Extinguisher Exchange"}"""),
            RawAssetType("10", "Fire Extinguisher"), RawVariant("112", "10", "CO2 5.0KG", productId: "905"),
            RawAsset("42896", "1491", "10", variantId: "112", servicedBy: "371"),
            RawAsset("42897", "1491", "10", result: "F", label: "Spare"),
            Raw("contenttypes", "51", """{"model":"asset"}"""),
            Raw("timelineevents", "1", """{"key":"general","type":"E","text":"Asset serviced.","object_id":42896}""",
                "{\"content_type\":" + To("ContentType", "51") + "}"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal(2, results["assets"].Mapped);

        var type = await db.AssetTypes.SingleAsync();
        Assert.Equal((10L, "Fire Extinguisher", "Portables"), (type.UptickId!.Value, type.Name, type.SequenceGroup));
        var variant = await db.AssetTypeVariants.SingleAsync();
        Assert.Equal((112L, type.Id, "5.0X C02 Extinguisher Exchange"), (variant.UptickId!.Value, variant.AssetTypeId, variant.DefaultReplacementProduct));

        var asset = await db.Assets.Include(a => a.AssetTypes).Include(a => a.LastServicedBy).SingleAsync(a => a.UptickId == 42896);
        Assert.Equal(("42896", "Fire Extinguisher CO2 5.0KG", "001", "Kitchen", "2", "Pass"),
            (asset.DisplayRef, asset.Name, asset.Ref, asset.Location, asset.Quantity, asset.Compliance));
        Assert.Equal((type.Id, variant.Id, "CO2 5.0KG", 3), (asset.AssetTypes.Single().Id, asset.AssetTypeVariantId!.Value, asset.Variant, asset.InspectionOrder!.Value));
        Assert.Equal((new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)),
            (asset.InstallationDate!.Value, asset.LastServiceDate!.Value));
        Assert.Equal("Wes Speedon (Field Tech)", asset.LastServicedBy!.Name);
        Assert.Equal(asset.Id, (await db.TimelineEvents.SingleAsync()).AssetId);

        var spare = await db.Assets.SingleAsync(a => a.UptickId == 42897);
        Assert.Equal(("Spare", "Fail", null), (spare.Name, spare.Compliance, spare.LastServicedById));
    }

    [Fact]
    public async Task Types_variants_and_assets_from_the_file_imports_are_claimed_not_added_again()
    {
        await using var db = DbContextFactory.Create();
        var client = new Client { Name = "Allied", UptickId = 2806 };
        var property = new Property { Name = "Big River", UptickId = 1491, Ref = "P-0016", Client = client };
        // Made by name from the assets export, then a variant from the variants export, then the asset
        var type = new AssetType { Name = "fire extinguisher" };
        var variant = new AssetTypeVariant { Name = "CO2 5.0KG", AssetType = type };
        var asset = new Asset { Name = "Old", UptickId = 42896, Property = property, AssetTypes = { type }, InternalNote = "x" };
        db.AddRange(client, property, type, variant, asset);
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawAssetType("10", "Fire Extinguisher"), RawVariant("112", "10", "CO2 5.0kg"), RawAsset("42896", "1491", "10", variantId: "112"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var types = await db.AssetTypes.ToListAsync();
        Assert.Equal((type.Id, 10L, "Fire Extinguisher"), (Assert.Single(types).Id, types[0].UptickId!.Value, types[0].Name));
        Assert.Equal((variant.Id, 112L), ((await db.AssetTypeVariants.SingleAsync()).Id, (await db.AssetTypeVariants.SingleAsync()).UptickId!.Value));
        var mapped = await db.Assets.SingleAsync();
        Assert.Equal((asset.Id, 42896L, variant.Id, null), (mapped.Id, mapped.UptickId!.Value, mapped.AssetTypeVariantId!.Value, mapped.InternalNote));
    }

    [Fact]
    public async Task An_asset_whose_property_isnt_held_waits_and_one_deleted_in_Uptick_goes_inactive()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"), RawAssetType("10", "Fire Extinguisher"),
            RawAsset("42896", "1491", "10"), RawAsset("42897", "777", "10"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal((1, 1), (results["assets"].Mapped!.Value, results["assets"].MapFailed));
        Assert.Contains("42897: Its property (777) isn't in FyreApp.", results["assets"].MapErrors);

        var deleted = await db.UptickRecords.SingleAsync(r => r.Resource == "assets" && r.UptickId == "42896");
        deleted.DeletedUtc = Now;
        deleted.MappedUtc = null;
        await db.SaveChangesAsync();
        await MapAllAsync(db);
        Assert.False((await db.Assets.SingleAsync()).IsActive);
    }

    // A property with one asset, mapped from Uptick
    private static async Task<AppDbContext> MappedAssetAsync()
    {
        var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawAssetType("10", "Fire Extinguisher"), RawAssetType("11", "Fire Blanket"), RawAsset("42896", "1491", "10"));
        await db.SaveChangesAsync();
        await MapAllAsync(db);
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task ChangeInUptickAsync(AppDbContext db, string id, string field, string json)
    {
        var raw = await db.UptickRecords.SingleAsync(r => r.Resource == "assets" && r.UptickId == id);
        var attributes = System.Text.Json.Nodes.JsonNode.Parse(raw.Attributes)!.AsObject();
        attributes[field] = System.Text.Json.Nodes.JsonNode.Parse(json);
        raw.Attributes = attributes.ToJsonString();
        raw.MappedUtc = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // Until cutover (the default): a run puts what was edited in FyreApp back to Uptick's, changed there or not
    [Fact]
    public async Task Until_cutover_a_run_puts_a_field_edited_in_FyreApp_back_to_Upticks()
    {
        await using var db = await MappedAssetAsync();
        var asset = await db.Assets.SingleAsync();
        asset.Location = "Plant room";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True((await db.LocalEdits.SingleAsync()).IsOpen);

        // A Refresh with nothing changed in Uptick
        await MapAllAsync(db);

        Assert.Equal("Kitchen", (await db.Assets.SingleAsync()).Location);
        var edit = await db.LocalEdits.SingleAsync();
        Assert.Equal((false, "Uptick"), (edit.IsOpen, edit.ClosedReason));

        // Edited again: a Re-map does the same
        asset = await db.Assets.SingleAsync();
        asset.Location = "Roof";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await MapAllAsync(db, remap: true);
        Assert.Equal("Kitchen", (await db.Assets.SingleAsync()).Location);
        Assert.All(await db.LocalEdits.ToListAsync(), e => Assert.False(e.IsOpen));
    }

    // After cutover (KeepFyreAppEdits on)
    [Fact]
    public async Task A_field_edited_in_FyreApp_survives_mapping_until_Uptick_changes_it()
    {
        await using var db = await MappedAssetAsync();
        var asset = await db.Assets.SingleAsync();
        asset.Location = "Plant room";
        await db.SaveChangesAsync();

        var edit = await db.LocalEdits.SingleAsync();
        Assert.Equal(("Asset", asset.Id, "Location", "Kitchen", "Plant room", true),
            (edit.Entity, edit.RecordId, edit.Field, edit.UptickValue, edit.FyreAppValue, edit.IsOpen));

        // A re-map, and a change in Uptick to another field: FyreApp's location stays
        await MapAllAsync(db, remap: true, keepEdits: true);
        await ChangeInUptickAsync(db, "42896", "internal_note", "\"Swap at next service\"");
        await MapAllAsync(db, keepEdits: true);
        asset = await db.Assets.SingleAsync();
        Assert.Equal(("Plant room", "Swap at next service"), (asset.Location, asset.InternalNote));
        Assert.True((await db.LocalEdits.SingleAsync()).IsOpen);

        // Changed in Uptick since: Uptick's wins and the edit closes
        await ChangeInUptickAsync(db, "42896", "location", "\"Store room\"");
        await MapAllAsync(db, keepEdits: true);
        Assert.Equal("Store room", (await db.Assets.SingleAsync()).Location);
        edit = await db.LocalEdits.SingleAsync();
        Assert.Equal((false, "Uptick"), (edit.IsOpen, edit.ClosedReason));
    }

    [Fact]
    public async Task An_asset_type_changed_in_FyreApp_is_kept_and_can_be_reset()
    {
        await using var db = await MappedAssetAsync();
        var asset = await db.Assets.Include(a => a.AssetTypes).SingleAsync();
        var extinguisher = asset.AssetTypes.Single();
        var blanket = await db.AssetTypes.SingleAsync(t => t.UptickId == 11);
        asset.AssetTypes.Clear();
        asset.AssetTypes.Add(blanket);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var edit = await db.LocalEdits.SingleAsync();
        Assert.Equal((LocalEditLog.AssetTypes, extinguisher.Id.ToString(), blanket.Id.ToString()), (edit.Field, edit.UptickValue, edit.FyreAppValue));

        await MapAllAsync(db, remap: true, keepEdits: true);
        Assert.Equal(blanket.Id, (await db.Assets.Include(a => a.AssetTypes).SingleAsync()).AssetTypes.Single().Id);
        db.ChangeTracker.Clear();

        // An admin puts it back to Uptick's
        asset = await db.Assets.Include(a => a.AssetTypes).SingleAsync();
        edit = await db.LocalEdits.SingleAsync();
        LocalEditLog.Reset(db, edit, asset, "admin", Now);
        db.RecordLocalEdits = false;
        await db.SaveChangesAsync();
        db.RecordLocalEdits = true;
        db.ChangeTracker.Clear();

        Assert.Equal(extinguisher.Id, (await db.Assets.Include(a => a.AssetTypes).SingleAsync()).AssetTypes.Single().Id);
        edit = await db.LocalEdits.SingleAsync();
        Assert.Equal((false, "Reset", "admin"), (edit.IsOpen, edit.ClosedReason, edit.ClosedBy));
    }

    // Every kind of field a migrated record has goes back from the text LocalEdit keeps (a task's status is an enum:
    // started in the field app, it's an edit an admin may put back)
    [Theory]
    [InlineData(typeof(ClientTaskStatus), ClientTaskStatus.Open)]
    [InlineData(typeof(ClientTaskStatus?), ClientTaskStatus.Completed)]
    [InlineData(typeof(int?), 7)]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(string), "Roof")]
    public void A_stored_value_parses_back_to_its_type(Type type, object value) =>
        Assert.Equal(value, LocalEditLog.Parse(LocalEditLog.Text(value), type));

    [Fact]
    public void Dates_and_decimals_parse_back_as_kept()
    {
        var when = new DateTime(2026, 10, 24, 1, 2, 3, DateTimeKind.Utc);
        var back = (DateTime)LocalEditLog.Parse(LocalEditLog.Text(when), typeof(DateTime?))!;
        Assert.Equal((when, DateTimeKind.Utc), (back, back.Kind));
        Assert.Equal(2.5m, LocalEditLog.Parse(LocalEditLog.Text(2.5m), typeof(decimal?)));
    }

    [Fact]
    public async Task Editing_back_to_Uptick_closes_the_edit_and_FyreApps_own_records_record_nothing()
    {
        await using var db = await MappedAssetAsync();
        var asset = await db.Assets.SingleAsync();
        asset.InspectionOrder = 9;
        await db.SaveChangesAsync();
        asset.InspectionOrder = 7;
        await db.SaveChangesAsync();
        var edit = await db.LocalEdits.SingleAsync();
        Assert.Equal(("3", "7", true), (edit.UptickValue, edit.FyreAppValue, edit.IsOpen));

        asset.InspectionOrder = 3;
        await db.SaveChangesAsync();
        Assert.Equal((false, "Reverted"), ((await db.LocalEdits.SingleAsync()).IsOpen, (await db.LocalEdits.SingleAsync()).ClosedReason));

        // Made in FyreApp: nothing in Uptick to keep it from
        var own = new Asset { Name = "New", PropertyId = asset.PropertyId };
        db.Assets.Add(own);
        await db.SaveChangesAsync();
        own.Location = "Roof";
        await db.SaveChangesAsync();
        Assert.Single(await db.LocalEdits.ToListAsync());
    }

    [Fact]
    public async Task An_asset_types_applicable_routines_and_excluded_levels_come_from_its_links()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(
            Raw("routineservicetypes", "10", """{"name":"10 - Portable and Wheeled Fire Extinguishers","is_active":true}"""),
            Raw("routineservicetypes", "11", """{"name":"11 - Fire Blankets","is_active":true}"""),
            Raw("routineserviceleveltypes", "15", """{"name":"Five-yearly","interval":5,"frequency":"Y","is_active":true}""",
                "{\"routineservicetype\":" + To("RoutineServiceType", "10") + "}"),
            RawAssetType("10", "Fire Extinguisher"),
            Raw("assettypes.links", "10", "{}",
                """{"applicable_routines":{"data":[{"type":"RoutineServiceType","id":"10"},{"type":"RoutineServiceType","id":"11"}]},"excluded_routinelevels":{"data":[{"type":"RoutineServiceLevelType","id":"15"}]}}"""));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var type = await db.AssetTypes.SingleAsync();
        var routines = (await FyreApp.Services.Routines.RoutineAssets.TypeRoutinesAsync(db, [type.Id]))[type.Id];
        Assert.Equal(new[] { "10 - Portable and Wheeled Fire Extinguishers", "11 - Fire Blankets" }, routines.Applicable.Order());
        Assert.Equal("10 - Portable and Wheeled Fire Extinguishers: Five-yearly", Assert.Single(routines.Excluded));

        // Uptick drops a routine from the type
        var links = await db.UptickRecords.SingleAsync(r => r.Resource == "assettypes.links");
        links.Relationships = """{"applicable_routines":{"data":[{"type":"RoutineServiceType","id":"11"}]},"excluded_routinelevels":{"data":[]}}""";
        links.MappedUtc = null;
        await db.SaveChangesAsync();
        await MapAllAsync(db);
        routines = (await FyreApp.Services.Routines.RoutineAssets.TypeRoutinesAsync(db, [type.Id]))[type.Id];
        Assert.Equal(("11 - Fire Blankets", 0), (Assert.Single(routines.Applicable), routines.Excluded.Count));
    }

    [Fact]
    public async Task Routine_service_types_levels_and_remark_types_are_mapped_and_claim_what_the_exports_made()
    {
        await using var db = DbContextFactory.Create();
        // From the routines export (by name)
        var made = new RoutineServiceType { Name = "10 - Portable and Wheeled Fire Extinguishers" };
        db.RoutineServiceTypes.Add(made);
        db.RoutineServiceLevels.Add(new RoutineServiceLevel { Name = "six-monthly", RoutineServiceType = made, IntervalMonths = 6 });
        var client = new Client { Name = "Allied", UptickId = 2806 };
        var property = new Property { Name = "Big River", UptickId = 1491, Ref = "P-0016", Client = client };
        db.AddRange(client, property);
        db.UptickRecords.AddRange(
            Raw("standards", "1", """{"name":"AS1851-2012"}"""), Raw("servicegroups", "4", """{"name":"Servicing - Portables & Fire Equipment"}"""),
            Raw("routineservicetypes", "10", """{"name":"10 - Portable and Wheeled Fire Extinguishers","standard_reference":"AS1851-2012 Section 10","is_active":true,"is_custom":false}""",
                "{\"standard\":" + To("Standard", "1") + ",\"servicegroup\":" + To("ServiceGroup", "4") + "}"),
            Raw("routineserviceleveltypes", "14", """{"name":"Six-monthly","display_code":"H","interval":6,"frequency":"M","service_rank":2,"is_default_enabled":true,"is_active":true}""",
                "{\"routineservicetype\":" + To("RoutineServiceType", "10") + "}"),
            Raw("routineserviceleveltypes", "15", """{"name":"Five-yearly","interval":5,"frequency":"Y","is_active":true}""",
                "{\"routineservicetype\":" + To("RoutineServiceType", "10") + "}"),
            RawAssetType("10", "Fire Extinguisher"),
            Raw("remarktypes", "723", """{"label":"1.02 - Battery missing","severity":10,"resolution":"Supply and install new batteries","is_owner_responsible":true,"is_active":true}""",
                "{\"type\":" + To("AssetType", "10") + "}"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var type = await db.RoutineServiceTypes.SingleAsync();
        Assert.Equal((made.Id, 10L, "AS1851-2012", "AS1851-2012 Section 10", "Servicing - Portables & Fire Equipment"),
            (type.Id, type.UptickId!.Value, type.Standard, type.StandardReference, type.ServiceGroup));
        var levels = await db.RoutineServiceLevels.OrderBy(l => l.UptickId).ToListAsync();
        Assert.Equal(2, levels.Count);
        Assert.Equal((14L, "Six-monthly", "H", 6, 2), (levels[0].UptickId!.Value, levels[0].Name, levels[0].DisplayCode, levels[0].IntervalMonths, levels[0].ServiceRank!.Value));
        Assert.Equal((type.Id, 60), (levels[1].RoutineServiceTypeId, levels[1].IntervalMonths));

        var remarkType = await db.RemarkTypes.SingleAsync();
        Assert.Equal(("1.02 - Battery missing", "Critical defect", true, (await db.AssetTypes.SingleAsync()).Id),
            (remarkType.Label, remarkType.SeverityLabel, remarkType.OwnerResponsible, remarkType.AssetTypeId!.Value));
    }

    private static UptickRecord RawTask(string id, string? propertyId, string status = "COMPLETE", string? assignedTo = null) => Raw("tasks", id,
        "{\"ref\":\"T-" + id + "\",\"name\":\"PM2026/10 Servicing - Portables & Fire Equipment\",\"description\":\"Oct 2026 PM\"," +
        "\"scope_of_works\":\"- 09 - Fire Hose Reels (Section 9): Annual\",\"due\":\"2026-10-31\",\"priority\":5," +
        "\"created\":\"2026-10-15T11:42:34+11:00\",\"is_active\":" + (status == "COMPLETE" ? "false" : "true") + "," +
        "\"status_changed_complete\":\"2026-10-15T15:22:53+11:00\",\"status_changed_inspected\":\"2026-10-15T15:15:25+11:00\"," +
        "\"invoice_note\":\"As per quote Q-2790\",\"technician_note\":\"\"}",
        "{\"status\":{\"data\":{\"id\":\"" + status + "\",\"type\":\"TaskStatus\"}},\"category\":" + To("TaskCategory", "1") +
        ",\"property\":" + To("Property", propertyId) + ",\"client\":" + To("Client", "2806") + ",\"assigned_to\":" + To("User", assignedTo) +
        ",\"servicegroup\":" + To("ServiceGroup", "4") + "}");

    private static UptickRecord RawServiceTask(string id, string taskId, string? assetId, string? levelId, string result = "P") => Raw("servicetasks", id,
        "{\"result\":\"" + result + "\",\"performed_date\":\"2026-10-15T15:15:18+11:00\",\"notes\":\"\"}",
        "{\"task\":" + To("Task", taskId) + ",\"item\":" + To("Asset", assetId) + ",\"routineserviceleveltype\":" + To("RoutineServiceLevelType", levelId) +
        ",\"technician\":" + To("User", "371") + ",\"routine\":" + To("Routine", "165899") + ",\"product\":" + To("Product", assetId == null ? "905" : null) + "}");

    [Fact]
    public async Task Tasks_and_their_service_tasks_are_mapped_and_the_task_page_shows_Upticks_fields()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawUser("371", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"),
            Raw("taskcategories", "1", """{"name":"I&T"}"""), Raw("servicegroups", "4", """{"name":"Servicing - Portables & Fire Equipment"}"""),
            RawAssetType("10", "Fire Hose Reel"), RawAsset("42896", "1491", "10"),
            Raw("routineservicetypes", "9", """{"name":"09 - Fire Hose Reels","is_active":true}"""),
            Raw("routineserviceleveltypes", "44", """{"name":"Annual","interval":12,"frequency":"M","is_active":true}""",
                "{\"routineservicetype\":" + To("RoutineServiceType", "9") + "}"),
            RawTask("46781", "1491", assignedTo: "371"), RawTask("46790", null, status: "READY"),
            RawServiceTask("216732", "46781", "42896", "44"), RawServiceTask("216733", "46781", null, null),
            RawServiceTask("216734", "99999", "42896", "44"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal(2, results["tasks"].Mapped);
        Assert.Equal((2, 1), (results["servicetasks"].Mapped!.Value, results["servicetasks"].MapFailed));
        Assert.Contains("216734: Its task (99999) isn't in FyreApp.", results["servicetasks"].MapErrors);

        var property = await db.Properties.SingleAsync(p => p.UptickId == 1491);
        var task = await db.ClientTasks.Include(t => t.AssignedTo).SingleAsync(t => t.UptickId == 46781);
        Assert.Equal(("T-46781", "46781", property.Id, property.ClientId, "I&T", ClientTaskStatus.Completed, false),
            (task.Ref, task.UptickId!.Value.ToString(), task.PropertyId, task.ClientId, task.Category, task.Status, task.IsActive));
        Assert.Equal(("Wes Speedon (Field Tech)", "Wes Speedon (Field Tech)", "- 09 - Fire Hose Reels (Section 9): Annual"),
            (task.AssignedTo!.Name, task.UptickTechnician, task.ScopeOfWorks));
        Assert.Equal((new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 15, 4, 22, 53, DateTimeKind.Utc)),
            (task.DueDateUtc!.Value, task.CompletedUtc!.Value));

        // No property in Uptick: on the placeholder property, as the tasks export did
        var orphan = await db.ClientTasks.Include(t => t.Property).SingleAsync(t => t.UptickId == 46790);
        Assert.Equal((FyreApp.Services.Tasks.UptickTaskRules.NoPropertySiteName, ClientTaskStatus.Open), (orphan.Property.Name, orphan.Status));

        var lines = await db.ServiceTasks.OrderBy(s => s.UptickId).ToListAsync();
        Assert.Equal(("09 - Fire Hose Reels: Annual", "Pass", (await db.Assets.SingleAsync()).Id, 165899L),
            (lines[0].Routine, lines[0].Result, lines[0].AssetId!.Value, lines[0].UptickRoutineId!.Value));
        Assert.Equal((null, 905L), (lines[1].AssetId, lines[1].UptickProductId!.Value));

        var fields = await FyreApp.Services.Tasks.TaskUptickFields.ForAsync(db, task, CancellationToken.None);
        Assert.Equal(("COMPLETE", "As per quote Q-2790", "Servicing - Portables & Fire Equipment", "2026-10-15"),
            (fields["Status"], fields["Invoice Note"], fields["Service Group"], fields["Performed Date"]));
        Assert.False(fields.ContainsKey("Technician Note"));
    }

    [Fact]
    public async Task A_tasks_time_visits_lines_and_stock_are_mapped_onto_it()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawUser("371", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"), RawUser("9", "Dwayne Page", "d@x.au", "FIELD"),
            RawTask("46781", "1491"),
            Raw("products", "704", """{"name":"2.5kg ABE Extinguisher","cost_price":"40.0000"}"""),
            Raw("tasksessiontypes", "1", """{"label":"Performing Task","is_onsite":true}"""),
            Raw("tasksessiontypes", "2", """{"label":"Travelling","is_onsite":false}"""),
            Raw("tasksessions", "900", """{"started":"2026-10-15T12:00:00+11:00","finished":"2026-10-15T14:49:00+11:00","hours":"2.8167","cost":"281.6700","multiplier_label":"Normal Time","is_approved":false,"is_submitted":true,"started_coord_lat":-28.1044,"started_coord_lng":153.4128,"trashed":null}""",
                "{\"task\":" + To("Task", "46781") + ",\"type\":" + To("TaskSessionType", "1") + ",\"technician\":" + To("User", "371") + "}"),
            Raw("tasksessions", "901", """{"started":"2026-10-15T11:30:00+11:00","finished":"2026-10-15T12:00:00+11:00","hours":"0.5000","trashed":"2026-10-16T09:00:00+11:00"}""",
                "{\"task\":" + To("Task", "46781") + ",\"type\":" + To("TaskSessionType", "2") + ",\"technician\":" + To("User", "371") + "}"),
            Raw("appointments", "20753", """{"status":"scheduled","scheduled_start":"2026-10-15T08:00:00+10:00","scheduled_end":"2026-10-15T10:00:00+10:00","subject":"PM2026/10 (P-0016)","notes":"Key at reception"}""",
                "{\"task\":" + To("Task", "46781") + "}"),
            Raw("appointments.links", "20753", "{}", """{"technicians":{"data":[{"type":"User","id":"371"},{"type":"User","id":"9"}]}}"""),
            Raw("subtasks", "5001", """{"order":1,"is_performed":true,"performed_date":"2026-10-15","service_task":true,"report_task":true,"billing_task":false,"deleted":null}""",
                "{\"task\":" + To("Task", "46781") + ",\"product\":" + To("Product", "704") + "}"),
            Raw("taskstocktransactions", "7001", """{"delta":"2.0000","cost_price":"40.0000","created":"2026-10-15T13:00:00+11:00"}""",
                "{\"task\":" + To("Task", "46781") + ",\"product\":" + To("Product", "704") + ",\"committed_by\":" + To("User", "371") + "}"),
            Raw("contenttypes", "88", """{"app_label":"servicing","model":"servicetask"}"""),
            Raw("taskstocktransactions", "7003", """{"delta":"-2.0000","cost_price":"40.0000","other_object_id":216800}""",
                "{\"task\":" + To("Task", "46781") + ",\"product\":" + To("Product", "704") + ",\"other_content_type\":" + To("ContentType", "88") + "}"),
            Raw("taskstocktransactions", "7002", """{"delta":"1.0000"}""", "{\"task\":" + To("Task", "99999") + "}"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);

        var task = await db.ClientTasks.SingleAsync();
        var vm = (await new FyreApp.Services.Tasks.TaskPageService(db).LoadAsync(task.Id, "time"))!;
        // The trashed session is kept but not shown
        var session = Assert.Single(vm.Sessions);
        Assert.Equal(("Performing Task", true, 2.8167m, "Wes Speedon (Field Tech)", new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc)),
            (session.Type, session.IsOnsite, session.Hours!.Value, session.UptickUser!.Name, session.StartedUtc));
        Assert.Equal((-28.1044, true), (session.StartedLatitude!.Value, session.IsSubmitted));
        Assert.Equal(2, await db.TaskSessions.CountAsync());

        var visit = Assert.Single(vm.Appointments);
        Assert.Equal(("Key at reception", new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc)), (visit.Notes, visit.StartUtc));
        Assert.Equal(["Dwayne Page", "Wes Speedon (Field Tech)"], visit.Technicians.Select(u => u.Name).Order());

        var line = Assert.Single(vm.Subtasks);
        Assert.Equal(("2.5kg ABE Extinguisher", true, new DateTime(2026, 10, 15)), (line.Product!.Name, line.IsPerformed, line.PerformedDate!.Value));

        Assert.Equal(2, vm.Stock.Count);
        var used = vm.Stock.Single(s => s.Quantity < 0);
        Assert.Equal(("servicetask", 216800L), (used.OtherType, used.OtherUptickId!.Value));
        var stock = vm.Stock.Single(s => s.Quantity > 0);
        Assert.Equal((2m, 40m, "Wes Speedon (Field Tech)"), (stock.Quantity, stock.CostPrice!.Value, stock.UptickUser!.Name));
        Assert.Contains("7002: Its task (99999) isn't in FyreApp.", results["taskstocktransactions"].MapErrors);
    }

    [Fact]
    public async Task Report_items_and_stock_used_on_service_lines_show_on_the_report_and_the_assets_history()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawUser("371", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"),
            RawAssetType("10", "Fire Extinguisher"), RawAsset("42896", "1491", "10"), RawTask("46781", "1491"),
            Raw("products", "704", """{"name":"2.5kg ABE Extinguisher"}"""),
            Raw("contenttypes", "88", """{"app_label":"servicing","model":"servicetask"}"""),
            RawServiceTask("216800", "46781", "42896", null),
            Raw("taskstocktransactions", "7003", """{"delta":"-1.0000","cost_price":"40.0000","other_object_id":216800}""",
                "{\"task\":" + To("Task", "46781") + ",\"product\":" + To("Product", "704") + ",\"other_content_type\":" + To("ContentType", "88") + "}"),
            Raw("reports", "53286", """{"ref":"R-53286","title":"Service Report","issued":"2026-10-15","inspected":"2026-10-14","compliant":false}""",
                "{\"task\":" + To("Task", "46781") + ",\"property\":" + To("Property", "1491") + "}"),
            Raw("reportitems", "9001", """{"ref":"002","label":"002 - Fire Blanket","location":"Kitchen","compliant":false,"inspected_date":"2026-10-14"}""",
                "{\"report\":" + To("Report", "53286") + ",\"asset\":" + To("Asset", "77777") + "}"),
            Raw("reportitems", "9000", """{"ref":"001","label":"001 - Fire Extinguisher","location":"Kitchen","barcode":"05801","compliant":true,"inspected_date":"2026-10-14"}""",
                "{\"report\":" + To("Report", "53286") + ",\"asset\":" + To("Asset", "42896") + "}"),
            Raw("reportitems", "9002", """{"label":"Orphan"}""", "{\"report\":" + To("Report", "1") + "}"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Contains("9002: Its report (1) isn't in FyreApp.", results["reportitems"].MapErrors);

        var report = await db.Reports.SingleAsync();
        var page = (await new FyreApp.Services.Lists.ReportListService(db).DetailsAsync(report.Id))!;
        // In the report's order, by ref; an asset FyreApp doesn't hold is still listed
        Assert.Equal(["001 - Fire Extinguisher", "002 - Fire Blanket"], page.Items.Select(i => i.Label));
        Assert.Equal((true, "05801", (int?)null), (page.Items[0].Compliant!.Value, page.Items[0].Barcode, page.Items[1].AssetId));

        var asset = await db.Assets.SingleAsync();
        var vm = (await new FyreApp.Services.Assets.AssetPageService(db, Moq.Mock.Of<FyreApp.Services.Timeline.ITimelineService>())
            .LoadAsync(asset.Id, "service", false))!;
        var (line, used) = Assert.Single(vm.ServiceLines);
        Assert.Equal(("Pass", "Wes Speedon (Field Tech)"), (line.Result, line.Technician!.Name));
        Assert.Equal(("2.5kg ABE Extinguisher", 1m), Assert.Single(used));
        Assert.Equal(report.Id, Assert.Single(vm.ReportLines).ReportId);
    }

    [Fact]
    public async Task Emails_are_mapped_with_the_records_they_are_about_and_a_tasks_dispatches()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawUser("3", "Office", "admin@fyrepower.com.au", "OFFICE"), RawTask("46781", "1491"),
            Raw("reports", "53286", """{"ref":"R-53286","title":"Service Report","issued":"2026-10-15"}""",
                "{\"task\":" + To("Task", "46781") + ",\"property\":" + To("Property", "1491") + "}"),
            Raw("invoices", "52965", """{"number":"IV000032831","status":"AUTHORISED"}""",
                "{\"task\":" + To("Task", "46781") + ",\"property\":" + To("Property", "1491") + "}"),
            Raw("contenttypes", "15", """{"app_label":"clients","model":"client"}"""),
            Raw("contenttypes", "28", """{"app_label":"tasks","model":"task"}"""),
            Raw("contenttypes", "84", """{"app_label":"billing","model":"invoice"}"""),
            Raw("contenttypes", "90", """{"app_label":"issuance","model":"issuance"}"""),
            Raw("correspondence", "62559", """
                {"subject":"Service at Big River (P-0016)","from_str":"\"Fyrepower via Uptick\" <no-reply@fyrepower.onuptick.com>",
                 "from_name":"Fyrepower via Uptick","to_str":"anita@alliedx.com.au","cc_str":"","bcc":["office@fyrepower.com.au"],
                 "reply_to_str":"admin@fyrepower.com.au","created":"2026-10-15T15:22:52+11:00","is_starred":false,
                 "body_html":"<p>Dear Belinda,</p>",
                 "attachments":{"P-0016-Service Report.pdf":"reports/53286/P-0016-Service Report.pdf","IV000032831.pdf":"invoices/52965/IV000032831.pdf",
                                "Pumpsets_Monthly.pdf":"https://uptick-logbooks.s3.amazonaws.com/media/a.pdf?X-Amz-Signature=abc"}}
                """, "{\"sent_by\":" + To("User", "3") + "}"),
            Raw("correspondence", "62560", """{"subject":"RE: Service","from_str":"Belinda <anita@alliedx.com.au>","created":"2026-10-16T09:00:00+11:00","body_html":"<p>Thanks</p>","attachments":{}}""",
                """{"sent_by":{"data":null}}"""),
            Raw("correspondenceobjectlinks", "1", """{"name":"Allied","object_id":2806}""",
                "{\"correspondence\":" + To("Correspondence", "62559") + ",\"object_contenttype\":" + To("ContentType", "15") + "}"),
            Raw("correspondenceobjectlinks", "2", """{"name":"T-46781","object_id":46781}""",
                "{\"correspondence\":" + To("Correspondence", "62559") + ",\"object_contenttype\":" + To("ContentType", "28") + "}"),
            Raw("correspondenceobjectlinks", "3", """{"name":"IV000032831","object_id":52965}""",
                "{\"correspondence\":" + To("Correspondence", "62559") + ",\"object_contenttype\":" + To("ContentType", "84") + "}"),
            Raw("correspondenceobjectlinks", "4", """{"name":"DIS-52042","object_id":52042}""",
                "{\"correspondence\":" + To("Correspondence", "62559") + ",\"object_contenttype\":" + To("ContentType", "90") + "}"),
            Raw("correspondenceobjectlinks", "5", """{"name":"T-46781","object_id":46781}""",
                "{\"correspondence\":" + To("Correspondence", "62560") + ",\"object_contenttype\":" + To("ContentType", "28") + "}"),
            Raw("correspondenceobjectlinks", "6", """{"name":"Gone","object_id":1}""",
                "{\"correspondence\":" + To("Correspondence", "99999") + ",\"object_contenttype\":" + To("ContentType", "28") + "}"),
            Raw("dispatches", "52042", """
                {"ref":"DIS-52042","status":"SENT","__str__":"52042: PM2026/10 Servicing","created":"2026-10-15T15:22:52+11:00","issue_invoice":true,
                 "email_data":[{"id":0,"to":["anita@alliedx.com.au"],"cc":[],"bcc":[],"from":"Fyrepower via Uptick","reply_to":"admin@fyrepower.com.au",
                   "subject":"Service at Big River (P-0016)","attention":"Belinda Boyd","body_html":"<p>Dear Belinda,</p>",
                   "attachments":[{"name":"P-0016-Service Report.pdf","path":"reports/None/P-0016-Service Report.pdf","type":"application/pdf","objects":["Report:53286"]}]}]}
                """, "{\"task\":" + To("Task", "46781") + ",\"sender\":" + To("User", "3") + ",\"invoice\":" + To("Invoice", "52965") + ",\"property\":" + To("Property", "1491") + "}"));
        db.UptickFiles.Add(new UptickFile
        {
            Source = "reports", SourceId = "53286", RecordType = "reports", RecordUptickId = 53286, Kind = FyreApp.Services.Uptick.Files.UptickFilePaths.Pdf, StorageKey = "uptick/reports/53286.pdf"
        });
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Contains("6: Its email (99999) isn't in FyreApp.", results["correspondenceobjectlinks"].MapErrors);

        var email = await db.Correspondence.SingleAsync(c => c.UptickId == 62559);
        Assert.Equal(("anita@alliedx.com.au", "office@fyrepower.com.au", "Office", new DateTime(2026, 10, 15, 4, 22, 52, DateTimeKind.Utc)),
            (email.To, email.Bcc, (await db.UptickUsers.FindAsync(email.SentById))!.Name, email.SentUtc!.Value));
        // A link out of Uptick is kept without its signature
        Assert.Equal("https://uptick-logbooks.s3.amazonaws.com/media/a.pdf", email.Attachments.Single(a => a.Name == "Pumpsets_Monthly.pdf").Path);

        var service = new FyreApp.Services.Lists.CorrespondenceListService(db);
        var page = (await service.DetailsAsync(email.Id))!;
        var task = await db.ClientTasks.SingleAsync();
        Assert.Equal([("Client", "Allied", "Clients", "Details"), ("Task", "T-46781", "Task", "Details"), ("Invoice", "IV000032831", "Billing", "Invoice"),
                ("Dispatch", "DIS-52042", null, null)],
            page.Records.Select(r => (r.Kind, r.Name, r.Controller, r.Action)));
        Assert.Equal(task.Id, page.Records[1].Id);
        var report = await db.Reports.SingleAsync();
        var pdf = await db.UptickFiles.SingleAsync();
        var attached = page.Attachments.Single(a => a.Name == "P-0016-Service Report.pdf");
        Assert.Equal((report.Id, "Reports", pdf.Id), (attached.Record!.Id!.Value, attached.Record.Controller, attached.FileId!.Value));
        // An invoice's PDF links to the invoice's page (its file isn't copied here)
        var invoice = await db.Invoices.SingleAsync();
        var invoicePdf = page.Attachments.Single(a => a.Name == "IV000032831.pdf");
        Assert.Equal((invoice.Id, "Invoice", (int?)null), (invoicePdf.Record!.Id!.Value, invoicePdf.Record.Action, invoicePdf.FileId));
        Assert.Null(page.Attachments.Single(a => a.Name == "Pumpsets_Monthly.pdf").Record);
        Assert.Equal(("DIS-52042", "SENT"), (page.Dispatch!.Ref, page.Dispatch.Status));

        var panel = await service.PanelAsync("tasks", 46781, 10);
        Assert.Equal(2, panel.Total);
        Assert.Equal(["RE: Service", "Service at Big River (P-0016)"], panel.Latest.Select(c => c.Subject));
        var (dispatch, emailId, attachments) = Assert.Single(panel.Dispatches);
        Assert.Equal((task.Id, "Office", true, email.Id), (dispatch.ClientTaskId!.Value, dispatch.Sender!.Name, dispatch.IssueInvoice, emailId!.Value));
        Assert.Equal(["anita@alliedx.com.au"], Assert.Single(dispatch.Emails).To);
        // The report not yet issued when the dispatch was made is found by its object
        Assert.Equal((report.Id, pdf.Id), (Assert.Single(attachments).Record!.Id!.Value, attachments[0].FileId!.Value));

        var list = await service.SearchAsync(new FyreApp.ViewModels.Lists.CorrespondenceFilter { Record = "clients", RecordId = 2806 });
        Assert.Equal((1, "Allied", "Clients"), (list.Total, list.RecordLink!.Name, list.RecordLink.Controller));
        Assert.Equal(3, list.Items[0].Attachments);
        Assert.Equal(1, (await service.SearchAsync(new FyreApp.ViewModels.Lists.CorrespondenceFilter { Search = "re: serv" })).Total);
    }

    [Fact]
    public async Task Quote_and_invoice_lines_and_a_remarks_history_show_on_their_pages()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawUser("371", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"),
            RawAssetType("10", "Fire Hydrant"), RawAsset("42896", "1491", "10"), RawTask("46781", "1491"), RawTask("46900", "1491"),
            Raw("products", "767", """{"name":"Hydrant Valve"}"""),
            Raw("routineservicetypes", "5", """{"name":"05 - Fire Hydrants","is_active":true}"""),
            Raw("remarks", "55218", """{"ref":"D-55218","status":"RESOLVED","description":"Valve leaking","is_active":false}""",
                "{\"asset\":" + To("Asset", "42896") + ",\"most_progressed_defectquote\":" + To("DefectQuote", "345") + "}"),
            Raw("remarkevents", "2", """{"event":"R","event_display":"Resolved","notes":"Valve replaced","created":"2026-10-20T10:00:00+11:00"}""",
                "{\"remark\":" + To("Remark", "55218") + ",\"account\":" + To("User", "371") + ",\"task\":" + To("Task", "46900") + "}"),
            Raw("remarkevents", "1", """{"event":"A","event_display":"Added","notes":"","created":"2026-10-15T15:14:49+11:00"}""",
                "{\"remark\":" + To("Remark", "55218") + ",\"account\":" + To("User", "371") + ",\"task\":" + To("Task", "46781") + "}"),
            Raw("defectquotes", "345", """{"ref":"Q-0333","status":"ACTIONED"}""", "{\"property\":" + To("Property", "1491") + "}"),
            Raw("defectquotelineitems", "11", """{"index":2,"description":"Labour","quantity":"1.0000","unit_price":"100.0000","subtotal":100.0,"gst":10.0,"total":110.0}""",
                "{\"quote\":" + To("DefectQuote", "345") + "}"),
            Raw("defectquotelineitems", "10", """{"index":1,"description":"Hydrant Valve 65mm","quantity":"2.0000","unit_price":"220.0000","cost_price":"90.0000","subtotal":440.0,"gst":44.0,"total":484.0}""",
                "{\"quote\":" + To("DefectQuote", "345") + ",\"product\":" + To("Product", "767") + ",\"asset\":" + To("Asset", "42896") + ",\"remark\":" + To("Remark", "55218") + "}"),
            Raw("invoices", "52965", """{"number":"IV000032831","status":"AUTHORISED","subtotal":"145.0000","gst":"14.5000","total":"159.5000"}""",
                "{\"task\":" + To("Task", "46781") + ",\"property\":" + To("Property", "1491") + "}"),
            Raw("invoicelineitems", "50834", """{"index":0,"description":"Minimum service fee","quantity":"1.0000","unit_price":"95.0000","subtotal":"95.0000","gst":"9.5000","total":"104.5000","account_code":"4-1080"}""",
                "{\"invoice\":" + To("Invoice", "52965") + "}"),
            Raw("invoicelineitems", "1", """{"index":0}""", "{\"invoice\":" + To("Invoice", "99999") + "}"),
            Raw("servicequotes", "43", """{"ref":"SQ-0043","status":"APPROVED","product_subtotal":280.0}""", "{\"property_object\":" + To("Property", "1491") + "}"),
            Raw("servicequoteproductlineitems", "1", """{"index":1,"description":"2.5Ab Extinguisher (NEW)","quantity":"2.0000","unit_price":"140.0000","subtotal":"280.0000","tax":"28.0000","total":"308.0000"}""",
                "{\"servicequote\":" + To("ServiceQuote", "43") + "}"),
            // Its own endpoint's id 1 as well: a different line
            Raw("servicequotefixedlineitems", "1", """{"index":1,"description":"Service 6x Hydrants","quantity":"1.0000","unit_price":"120.0000","annual_subtotal":"120.0000","annual_tax":"12.0000"}""",
                "{\"servicequote\":" + To("ServiceQuote", "43") + ",\"routineservicetype\":" + To("RoutineServiceType", "5") + "}"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Contains("1: Its invoice (99999) isn't in FyreApp.", results["invoicelineitems"].MapErrors);

        var pages = new FyreApp.Services.Billing.BillingPageService(db);
        var quote = (await pages.QuoteAsync((await db.DefectQuotes.SingleAsync()).Id))!;
        Assert.Equal(["Hydrant Valve 65mm", "Labour"], quote.Lines.Select(l => l.Description));
        var line = quote.Lines[0];
        Assert.Equal((2m, 220m, 90m, 44m, "Hydrant Valve", "D-55218"), (line.Quantity, line.UnitPrice!.Value, line.CostPrice!.Value, line.Tax!.Value,
            line.Product!.Name, line.Remark!.DisplayRef));
        Assert.Equal("D-55218", Assert.Single(quote.Remarks).DisplayRef);

        var invoice = (await pages.InvoiceAsync((await db.Invoices.SingleAsync()).Id))!;
        Assert.Equal(("Minimum service fee", "4-1080", 95m), (Assert.Single(invoice.Lines).Description, invoice.Lines[0].AccountCode, invoice.Lines[0].Subtotal!.Value));

        var sales = (await pages.SalesQuoteAsync((await db.ServiceQuotes.SingleAsync()).Id))!;
        // The routine service first, priced by the year
        Assert.Equal([("fixed", "05 - Fire Hydrants", 132m), ("product", null, 308m)],
            sales.Lines.Select(l => (l.Kind, l.RoutineServiceType?.Name, l.Total!.Value)));

        var history = await db.RemarkEvents.Include(e => e.ClientTask).OrderBy(e => e.WhenUtc).ToListAsync();
        Assert.Equal([("Added", "46781"), ("Resolved", "46900")], history.Select(e => (e.Label!, e.ClientTask!.UptickId!.Value.ToString())));
        Assert.Equal(("Valve replaced", "Wes Speedon (Field Tech)"), (history[1].Notes, (await db.UptickUsers.FindAsync(history[1].UptickUserId))!.Name));
    }

    [Fact]
    public async Task A_task_FyreApp_holds_is_updated_in_place()
    {
        await using var db = DbContextFactory.Create();
        var client = new Client { Name = "Allied", UptickId = 2806 };
        var property = new Property { Name = "Big River", UptickId = 1491, Ref = "P-0016", Client = client };
        var task = new ClientTask { Client = client, Property = property, Title = "Old", UptickId = 46781 };
        db.AddRange(client, property, task);
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"), RawTask("46781", "1491"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var mapped = await db.ClientTasks.SingleAsync();
        Assert.Equal((task.Id, 46781L, "PM2026/10 Servicing - Portables & Fire Equipment"), (mapped.Id, mapped.UptickId!.Value, mapped.Title));
        var fields = await FyreApp.Services.Tasks.TaskUptickFields.ForAsync(db, mapped, CancellationToken.None);
        Assert.Equal(("COMPLETE", false), (fields["Status"], fields.ContainsKey("Tags")));
        Assert.Equal((new DateTime(2026, 10, 15), (DateTime?)null), (mapped.PerformedDate!.Value, mapped.ScheduledDate));
    }

    [Fact]
    public async Task Remarks_and_reports_are_mapped_with_their_asset_task_repair_and_quote()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            RawUser("371", "Wes Speedon (Field Tech)", "wes@fyrepower.com.au", "FIELD"),
            Raw("taskcategories", "1", """{"name":"I&T"}"""), Raw("taskcategories", "3", """{"name":"Repair"}"""),
            RawAssetType("10", "Fire Hose Reel"), RawAsset("42896", "1491", "10"),
            Raw("remarktypes", "720", """{"label":"00 - Custom note","severity":0,"is_active":true}"""),
            RawTask("46781", "1491"), RawTask("46900", "1491", status: "READY"),
            Raw("defectquotes", "3001", """{"ref":"Q-2790","status":"APPROVED","date":"2026-10-16"}""", "{\"property\":" + To("Property", "1491") + "}"),
            Raw("remarks", "55218", """{"ref":"D-55218","status":"NEEDS_QUOTING","severity":10,"description":"Hose cracked","location":"","is_active":true,"identified":"2026-10-15T15:14:49+11:00"}""",
                "{\"type\":" + To("RemarkType", "720") + ",\"asset\":" + To("Asset", "42896") + ",\"most_progressed_defectquote\":" + To("DefectQuote", "3001") + "}"),
            Raw("remarks", "55219", """{"status":"RESOLVED","is_active":false}""", "{\"asset\":" + To("Asset", "77777") + "}"),
            Raw("remarkevents", "71532", """{"event":"A"}""", "{\"task\":" + To("Task", "46781") + ",\"remark\":" + To("Remark", "55218") + "}"),
            Raw("reporttypes", "4", """{"name":"Service Report"}"""),
            Raw("reports", "53286", """{"ref":"R-53286","title":"","issued":"2026-10-15","inspected":"2026-10-14","compliant":true,"is_published":true,"is_amendment":false,"general_note":"All good"}""",
                "{\"task\":" + To("Task", "46781") + ",\"type\":" + To("ReportType", "4") + ",\"property\":" + To("Property", "1491") + ",\"technician\":" + To("User", "371") + "}"));
        // The repair: a service task done for the remark on task 46900, made a Repair task below
        var repair = RawServiceTask("216800", "46900", "42896", null);
        repair.Relationships = repair.Relationships.Replace("{\"task\"", "{\"remark\":" + To("Remark", "55218") + ",\"task\"");
        db.UptickRecords.Add(repair);
        await db.SaveChangesAsync();
        // Make 46900 a Repair task
        var raw46900 = await db.UptickRecords.SingleAsync(r => r.Resource == "tasks" && r.UptickId == "46900");
        raw46900.Relationships = raw46900.Relationships.Replace(To("TaskCategory", "1"), To("TaskCategory", "3"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal((1, 1), (results["remarks"].Mapped!.Value, results["remarks"].MapFailed));
        Assert.Contains("55219: Its asset (77777) isn't in FyreApp.", results["remarks"].MapErrors);

        var asset = await db.Assets.SingleAsync();
        var remark = await db.Remarks.SingleAsync();
        Assert.Equal((55218L, "D-55218", asset.PropertyId, asset.Id, "00 - Custom note", "Needs Quoting", "Critical defect"),
            (remark.UptickId!.Value, remark.DisplayRef, remark.PropertyId, remark.AssetId!.Value, remark.RemarkType, remark.Status, remark.SeverityLabel));
        Assert.Equal(((await db.ClientTasks.SingleAsync(t => t.UptickId == 46781)).Id, "Q-2790", "APPROVED", "T-46900", "READY"),
            (remark.RaisedOnTaskId!.Value, remark.QuoteRef, remark.QuoteStatus, remark.RepairTaskRef, remark.RepairTaskStatus));
        Assert.Equal((await db.ClientTasks.SingleAsync(t => t.UptickId == 46900)).Id, remark.RepairTaskId);
        Assert.Equal(((await db.DefectQuotes.SingleAsync()).Id, new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc)),
            (remark.DefectQuoteId!.Value, remark.QuotedDate!.Value));

        var report = await db.Reports.SingleAsync();
        var task = await db.ClientTasks.SingleAsync(t => t.UptickId == 46781);
        Assert.Equal(("R-53286", "Service Report", task.Id, "T-46781", "Wes Speedon (Field Tech)", true, "All good"),
            (report.Ref, report.ReportType, report.ClientTaskId!.Value, report.TaskRef, report.Technician, report.Published, report.GeneralNote));
        Assert.Equal((new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), true), (report.IssuedDate!.Value, report.Compliant!.Value));

        // A report deleted in Uptick leaves FyreApp
        var raw = await db.UptickRecords.SingleAsync(r => r.Resource == "reports");
        raw.DeletedUtc = Now;
        raw.MappedUtc = null;
        await db.SaveChangesAsync();
        await MapAllAsync(db);
        Assert.Empty(await db.Reports.ToListAsync());
    }

    [Fact]
    public async Task Defect_and_sales_quotes_are_mapped_at_their_property_with_their_task_and_totals()
    {
        await using var db = DbContextFactory.Create();
        db.DefectQuotes.Add(new DefectQuote { Reference = "Q-0970", UptickId = 999, PropertyRef = "P-0016", Total = 1m });
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"), RawTask("9943", "1491"),
            Raw("servicegroups", "4", """{"name":"Plumbing & Backflow"}"""),
            Raw("defectquotes", "999", """{"ref":"Q-0970","date":"2023-09-25","status":"ACTIONED","due_date":"2023-10-25","description":"Exchange expired extinguishers","expiry_date":"2023-10-25","scope_of_works":"As per service report","authorisation_date":"2023-09-26T16:35:44.751217+10:00","status_changed_submitted":"2023-09-25T10:05:39.331441+10:00"}""",
                "{\"task\":" + To("Task", "9943") + ",\"property\":" + To("Property", "1491") + ",\"servicegroup\":" + To("ServiceGroup", "4") + "}"),
            Raw("defectquotelineitems", "1", """{"gst":3.0,"total":33.0,"subtotal":30.0}""", "{\"quote\":" + To("DefectQuote", "999") + "}"),
            Raw("defectquotelineitems", "2", """{"gst":29.5,"total":324.5,"subtotal":295.0}""", "{\"quote\":" + To("DefectQuote", "999") + "}"),
            Raw("defectquotes", "1000", """{"ref":"Q-0971"}""", "{\"property\":" + To("Property", "8888") + "}"),
            Raw("documenttemplates", "3", """{"name":"Service Quote (Routines)"}"""),
            Raw("servicequotes", "99", """{"ref":"SQ-0099","status":"EXPIRED","created":"2023-11-13T12:11:00.493378+11:00","template_name":"","description":"5 Yearly Overhaul of Hydrants","expiry_date":"2024-11-12","product_subtotal":415.0,"property_address":{"display":""}}""",
                "{\"property_object\":" + To("Property", "1491") + ",\"documenttemplate\":" + To("DocumentTemplate", "3") + "}"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal((1, 1), (results["defectquotes"].Mapped!.Value, results["defectquotes"].MapFailed));

        var property = await db.Properties.SingleAsync();
        var task = await db.ClientTasks.SingleAsync();
        var quote = await db.DefectQuotes.SingleAsync();
        Assert.Equal((999L, property.Id, task.Id, "Q-0970", "ACTIONED", "P-0016", "Allied", "Plumbing & Backflow"),
            (quote.UptickId!.Value, quote.PropertyId!.Value, quote.ClientTaskId!.Value, quote.Reference, quote.Status, quote.PropertyRef,
                quote.ClientName, quote.ServiceGroup));
        Assert.Equal((325m, 32.5m, 357.5m), (quote.Subtotal!.Value, quote.Tax!.Value, quote.Total!.Value));
        Assert.Equal((new DateTime(2023, 9, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2023, 9, 26, 0, 0, 0, DateTimeKind.Utc)),
            (quote.Date!.Value, quote.AuthorisedDate!.Value));

        var sales = await db.ServiceQuotes.SingleAsync();
        Assert.Equal((99L, property.Id, "SQ-0099", "EXPIRED", "Big River Trade Centre", "Service Quote (Routines)", 415m, null),
            (sales.UptickId!.Value, sales.PropertyId!.Value, sales.Ref, sales.Status, sales.PropertyName, sales.Template, sales.Subtotal!.Value, sales.Address));
        Assert.Equal(new DateTime(2023, 11, 13, 0, 0, 0, DateTimeKind.Utc), sales.Created);
    }

    [Fact]
    public async Task Invoices_and_credit_notes_are_mapped_with_their_property_task_and_credits()
    {
        await using var db = DbContextFactory.Create();
        db.Invoices.Add(new Invoice { Number = "IV000032831", UptickId = 30761, PropertyRef = "P-0016", Status = "DRAFT" });
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"), RawTask("46781", "1491"),
            Raw("billingcards", "23384", """{"name":"Allied OT Pty Limited"}"""),
            Raw("invoices", "30761", """{"gst":"14.5000","ref":"P-0016","date":"2026-10-15","total":"159.5000","number":"IV000032831","status":"PAID","due_date":"2026-10-22","subtotal":"145.0000","description":"P-0016 Allied"}""",
                "{\"task\":" + To("Task", "46781") + ",\"client\":" + To("Client", "2806") + ",\"property\":" + To("Property", "1491") + ",\"billingcard\":" + To("BillingCard", "23384") + "}"),
            Raw("invoices", "30762", """{"number":"IV000032832","status":"AUTHORISED","total":"55.0000"}""",
                "{\"task\":{\"data\":null},\"client\":" + To("Client", "2806") + ",\"property\":{\"data\":null}}"),
            Raw("invoices", "30763", """{"number":"IV000032833"}""", "{\"property\":" + To("Property", "8888") + "}"),
            Raw("creditnotes", "1", """{"number":"CN-31040","status":"CREDIT","date":"2026-10-16","total":"126.5000","deleted":null}""", "{\"invoice\":" + To("Invoice", "30761") + "}"),
            Raw("creditnotes", "2", """{"number":"CN-31041","status":"VOIDED","total":"10.0000","deleted":null}""", "{\"invoice\":" + To("Invoice", "30761") + "}"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal((2, 1), (results["invoices"].Mapped!.Value, results["invoices"].MapFailed));

        var property = await db.Properties.SingleAsync();
        var task = await db.ClientTasks.SingleAsync();
        var invoice = await db.Invoices.SingleAsync(i => i.UptickId == 30761);
        Assert.Equal((property.Id, task.Id, "T-46781", "PAID", "Allied", "Allied OT Pty Limited", 145m, 14.5m, 159.5m, 126.5m),
            (invoice.PropertyId!.Value, invoice.ClientTaskId!.Value, invoice.TaskRef, invoice.Status, invoice.ClientName, invoice.BillingCardName,
                invoice.Subtotal!.Value, invoice.Tax!.Value, invoice.Total!.Value, invoice.CreditTotal!.Value));
        var clientOnly = await db.Invoices.SingleAsync(i => i.UptickId == 30762);
        Assert.Equal((null, null, "Allied"), (clientOnly.PropertyId, clientOnly.ClientTaskId, clientOnly.ClientName));

        var notes = await db.CreditNotes.OrderBy(c => c.UptickId).ToListAsync();
        Assert.All(notes, n => Assert.Equal((invoice.Id, "IV000032831", "P-0016"), (n.InvoiceId!.Value, n.InvoiceNumber, n.PropertyRef)));
        Assert.Equal(("CN-31040", "CREDIT", 126.5m), (notes[0].Number, notes[0].Status, notes[0].Total!.Value));
    }

    [Fact]
    public async Task Billing_cards_contracts_and_lines_are_mapped_and_linked_to_their_property()
    {
        await using var db = DbContextFactory.Create();
        db.BillingCards.Add(new BillingCard { Name = "Old", UptickId = 157 });
        var client = RawClient("2806", "Allied");
        client.Relationships = "{\"billingcard\":" + To("BillingCard", "336") + "}";
        db.UptickRecords.AddRange(client, RawProperty("1491", "P-0016", "2806"),
            Raw("accountingorganisations", "1", """{"name":"Fyrepower Fire Protection"}"""),
            Raw("billingcards", "157", """{"abn":"20 642 908 601","name":"Allied OT Pty Limited","is_active":true,"postal_email":"anita@alliedx.com.au","postal_mobile":"0410453809","postal_address":{"display":"","streetline":"PO Box 1","city":"Burleigh Heads","state":"QLD","postal_code":"4220"}}""",
                "{\"accounting_organisation\":" + To("AccountingOrganisation", "1") + "}"),
            Raw("billingcards", "336", """{"name":"Allied Head Office","is_active":true}"""),
            Raw("invoices", "1", """{"number":"IV1"}""", "{\"billingcard\":" + To("BillingCard", "157") + "}"),
            Raw("products", "815", """{"name":"FIP Monthly","type":"S"}"""),
            Raw("billingcontracts", "2", """{"name":"NTS","type":"F","notes":"FIP only","is_active":true,"recurrence":"Q","invoice_mode":"A","start_date":"2022-09-01","annual_value":"300.0000"}""",
                "{\"property\":" + To("Property", "1491") + ",\"billingcard\":" + To("BillingCard", "336") + "}"),
            Raw("billingcontractlineitems", "3", """{"type":"V","visible":true,"unit_price":"75.0000","description":"Fip Monthly"}""",
                "{\"product\":" + To("Product", "815") + ",\"billingcontract\":" + To("BillingContract", "2") + "}"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var card = await db.BillingCards.SingleAsync(c => c.UptickId == 157);
        Assert.Equal(("Allied OT Pty Limited", "20 642 908 601", "PO Box 1 Burleigh Heads QLD 4220", 1, "Fyrepower Fire Protection"),
            (card.Name, card.BusinessNumber, card.PostalAddress, card.NumberOfInvoices!.Value, card.AccountingOrganisation));
        var property = await db.Properties.Include(p => p.Client).SingleAsync();
        Assert.Equal((card.Id, (await db.BillingCards.SingleAsync(c => c.UptickId == 336)).Id),
            (property.BillingCardId!.Value, property.Client.BillingCardId!.Value));

        var contract = await db.BillingContracts.SingleAsync();
        Assert.Equal((property.Id, "NTS", "Fixed", "Quarterly", "Advance", 300m, "Allied Head Office", true),
            (contract.PropertyId!.Value, contract.Name, contract.Type, contract.Frequency, contract.InvoiceMode, contract.AnnualValue!.Value,
                contract.BillingCardName, contract.Active));
        var line = await db.BillingContractLineItems.SingleAsync();
        Assert.Equal((contract.Id, "FIP Monthly", 75m, "Per visit"), (line.BillingContractId!.Value, line.Product, line.UnitPrice!.Value, line.Type));
    }

    [Fact]
    public async Task Security_groups_contractors_suppliers_and_accreditations_are_mapped()
    {
        await using var db = DbContextFactory.Create();
        db.Contractors.Add(new Contractor { Name = "Old", UptickId = 6, Skills = "Sprinklers" });
        db.UptickRecords.AddRange(RawUser("9", "Dwayne Page", "dwayne@fyrepower.com.au", "FIELD"),
            Raw("groups", "8", """{"name":"Technician - Higher Permissions"}"""),
            Raw("contractorcategories", "1", """{"name":"General"}"""),
            Raw("contractors", "6", """{"name":"Kennedy Fire","email":"fyrepowernotifications@gmail.com","email_cc":"office@fyrepower.com.au","is_active":false,"contact_bh":"07 5555 0000","is_starred":true}""",
                "{\"category\":" + To("ContractorCategory", "1") + "}"),
            Raw("accountingorganisations", "1", """{"name":"Fyrepower Fire Protection"}"""),
            Raw("suppliers", "99", """{"abn":"","name":"IFYS Ltd","is_active":true,"payment_day":14,"accounts_name":"Accounts","payment_terms":"DAYSAFTERDATE","accounts_email":"propertyadmin@ifys.com.au"}""",
                "{\"accounting_organisation\":" + To("AccountingOrganisation", "1") + "}"),
            Raw("accreditationtypes", "4", """{"name":"Licence No.:"}"""),
            Raw("accreditations", "67", """{"expiry":"2027-06-30","number":"QBCC 15366855 | FPA 25023","status":"ACTIVE"}""",
                "{\"property\":{\"data\":null},\"technician\":" + To("User", "9") + ",\"accreditationtype\":" + To("AccreditationType", "4") + "}"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        Assert.Equal((8L, "Technician - Higher Permissions"), (await db.SecurityGroups.SingleAsync()) is var g ? (g.UptickId!.Value, g.Name) : default);
        var contractor = await db.Contractors.SingleAsync();
        Assert.Equal((6L, "Kennedy Fire", "General", "07 5555 0000", false, true, "Sprinklers"),
            (contractor.UptickId!.Value, contractor.Name, contractor.Category, contractor.Phone, contractor.Active, contractor.Starred, contractor.Skills));
        var supplier = await db.Suppliers.SingleAsync();
        Assert.Equal(("IFYS Ltd", null, "14 days after invoice date", "Fyrepower Fire Protection"),
            (supplier.Name, supplier.BusinessNumber, supplier.PaymentTermsLabel, supplier.AccountingOrganisation));
        var user = await db.UptickUsers.SingleAsync();
        var accreditation = await db.Accreditations.SingleAsync();
        Assert.Equal((user.Id, "Dwayne Page", "Licence No.:", "ACTIVE", new DateTime(2027, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            (accreditation.UptickUserId!.Value, accreditation.Technician, accreditation.Type, accreditation.Status, accreditation.Expiry!.Value));
    }

    [Fact]
    public async Task Extra_fields_schedules_and_form_responses_are_mapped_and_linked()
    {
        await using var db = DbContextFactory.Create();
        db.ExtraFields.Add(new ExtraField { UptickId = 5, Label = "Old label", ContentType = "51" });
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawTask("46781", "1491"),
            RawUser("6", "Shea Flanigan", "shea@fyrepower.com.au", "FIELD"),
            Raw("properties", "1491", """{"ref":"P-0016","name":"Big River Trade Centre","status":"ACTIVE","extra_fields":{"fpaa_fsa_no":"FSA 1234","blank":"","council":"gccc"}}""",
                "{\"client\":" + To("Client", "2806") + "}"),
            Raw("extrafields", "5", """{"name":"fpaa_fsa_no","label":"FPAA FSA No.","order":3,"widget":"text","default":"","help_text":"","is_required":false,"field_settings":{}}""",
                "{\"content_type\":" + To("ContentType", "19") + "}"),
            Raw("extrafields", "6", """{"name":"blank","label":"Blank","order":4,"widget":"text"}""", "{\"content_type\":" + To("ContentType", "19") + "}"),
            Raw("extrafields", "7", """{"name":"council","label":"Local Government","order":5,"widget":"choice","field_settings":{"choices":[{"label":"Gold Coast City Council","value":"gccc"}]}}""",
                "{\"content_type\":" + To("ContentType", "19") + "}"),
            Raw("propertyschedules", "3", """{"number":"OP-77","date":"2019-05-01","type":"OP","type_display":"Occupancy Permit or Certificate of Occupancy","issuer":"GCCC","location":"Fire panel"}""",
                "{\"property\":" + To("Property", "1491") + "}"),
            Raw("dynamicformtemplates", "1001", """{"name":"Queensland Form 72","is_editable":true}"""),
            Raw("dynamicformtemplateversions", "1004", """{"created":"2021-04-15T10:32:59.322476+10:00"}""", "{\"template\":" + To("FormTemplate", "1001") + "}"),
            Raw("dynamicformresponses", "59", """{"ref":"F-00059","submitted":"2022-01-12T14:09:08.547246+11:00","is_complete":false,"target_object_id":"46781","values":{}}""",
                "{\"submitter\":" + To("User", "6") + ",\"template_version\":" + To("FormTemplateVersion", "1004") +
                ",\"target_contenttype\":" + To("ContentType", "28") + "}"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var field = await db.ExtraFields.SingleAsync(f => f.UptickId == 5);
        Assert.Equal((1, "19", "FPAA FSA No.", "fpaa_fsa_no", "{}"), (await db.ExtraFields.CountAsync(f => f.UptickId == 5), field.ContentType, field.Label, field.Name, field.FieldSettings));
        var property = await db.Properties.SingleAsync();
        var values = await FyreApp.Services.Imports.UptickReferenceValues.ExtraFieldsAsync(db, ExtraField.PropertyContentType, "properties", property.UptickId);
        Assert.Equal(new[] { ("FPAA FSA No.", "FSA 1234", (string?)null), ("Local Government", "Gold Coast City Council", null) },
            values.Select(v => (v.Label, v.Value, v.HelpText)));

        var schedule = await db.PropertySchedules.SingleAsync();
        Assert.Equal((property.Id, "OP-77", "Occupancy Permit or Certificate of Occupancy", "GCCC", "Fire panel"),
            (schedule.PropertyId!.Value, schedule.Number, schedule.Type, schedule.Issuer, schedule.Location));

        var form = await db.DynamicFormResponses.SingleAsync();
        var task = await db.ClientTasks.SingleAsync();
        var user = await db.UptickUsers.SingleAsync();
        Assert.Equal((59L, "F-00059", task.Id, user.Id, "Queensland Form 72", false),
            (form.UptickId!.Value, form.Ref, form.ClientTaskId!.Value, form.UptickUserId!.Value, form.TemplateName, form.IsComplete));
    }

    [Theory]
    [InlineData("NEEDS_QUOTING", "Needs Quoting")]
    [InlineData("RESOLVED", "Resolved")]
    [InlineData("PENDING_SUBMISSION", "Pending Submission")]
    [InlineData("EXPIRED_DECLINED", "Expired/Declined")]
    public void A_remark_status_reads_as_the_export_wrote_it(string uptick, string label) =>
        Assert.Equal(label, RemarkMapper.StatusLabel(uptick));

    [Fact]
    public async Task Routine_services_their_levels_and_routines_are_mapped_with_schedules_and_tasks()
    {
        await using var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"),
            Raw("taskcategories", "1", """{"name":"I&T"}"""), Raw("servicegroups", "4", """{"name":"Servicing - Portables & Fire Equipment"}"""),
            Raw("contractors", "5", """{"name":"CR&K Kay Plumbing"}"""),
            RawAssetType("10", "Fire Hose Reel"), RawAsset("42896", "1491", "10"),
            Raw("routineservicetypes", "9", """{"name":"09 - Fire Hose Reels","is_active":true}"""),
            Raw("routineserviceleveltypes", "44", """{"name":"Annual","interval":12,"frequency":"M","is_active":true}""",
                "{\"routineservicetype\":" + To("RoutineServiceType", "9") + "}"),
            Raw("routineserviceleveltypes", "45", """{"name":"Six-monthly","interval":6,"frequency":"M","is_active":true}""",
                "{\"routineservicetype\":" + To("RoutineServiceType", "9") + "}"),
            Raw("routineservices", "18996", """{"start_date":"2026-10-01","annual_test_date":"2026-10-01","end_date":null,"is_active":true,"created":"2026-10-15T11:42:19+11:00"}""",
                "{\"type\":" + To("RoutineServiceType", "9") + ",\"property\":" + To("Property", "1491") + ",\"servicegroup\":" + To("ServiceGroup", "4") +
                ",\"contractor\":" + To("Contractor", "5") + "}"),
            Raw("routineservicelevels", "22231", """{"is_active":true,"estimated_duration":"0:30:00"}""",
                "{\"type\":" + To("RoutineServiceLevelType", "44") + ",\"routineservice\":" + To("RoutineService", "18996") + "}"),
            Raw("routineservicelevels", "22232", """{"is_active":false}""",
                "{\"type\":" + To("RoutineServiceLevelType", "45") + ",\"routineservice\":" + To("RoutineService", "18996") + "}"),
            Raw("routines", "165899", """{"due":"2026-10-31","status":"C","completed_date":"2026-10-15","display_name":"09 - Fire Hose Reels: Annual","tolerance_period_start":"2026-08-01","tolerance_period_end":"2026-12-01"}""",
                "{\"routineservicelevel\":" + To("RoutineServiceLevel", "22231") + "}"),
            Raw("routines", "165900", """{"due":"2099-10-31","status":"P","display_name":"09 - Fire Hose Reels: Annual"}""",
                "{\"routineservicelevel\":" + To("RoutineServiceLevel", "22231") + "}"),
            RawTask("46781", "1491"), RawServiceTask("216732", "46781", "42896", "44"));
        await db.SaveChangesAsync();

        var results = await MapAllAsync(db);
        Assert.Equal((1, 2, 2), (results["routineservices"].Mapped!.Value, results["routineservicelevels"].Mapped!.Value, results["routines"].Mapped!.Value));

        var service = await db.RoutineServices.Include(s => s.Levels).SingleAsync();
        Assert.Equal((18996L, "Annual", "CR&K Kay Plumbing", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
            (service.UptickId!.Value, service.Levels.Single().Name, service.Subcontractor, service.StartDate));

        // One schedule for the property's annual routines, which the routine task covers
        var schedule = await db.MaintenanceSchedules.Include(s => s.MaintenanceInterval).Include(s => s.RoutineLevels).SingleAsync();
        Assert.Equal((12, "Annual"), (schedule.MaintenanceInterval.Months, schedule.RoutineLevels.Single().Name));

        var task = await db.ClientTasks.Include(t => t.CoveredSchedules).SingleAsync();
        Assert.Equal(schedule.Id, task.CoveredSchedules.Single().Id);
        var done = await db.Routines.SingleAsync(r => r.UptickId == 165899);
        Assert.Equal((RoutineStatus.Complete, service.Id, schedule.Id, task.Id, "Servicing - Portables & Fire Equipment", 30),
            (done.Status, done.RoutineServiceId!.Value, done.MaintenanceScheduleId!.Value, done.ClientTaskId!.Value, done.ServiceGroup, done.EstimatedMinutes!.Value));
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), done.ToleranceEnd);
        Assert.Equal(RoutineStatus.Pending, (await db.Routines.SingleAsync(r => r.UptickId == 165900)).Status);

        // Set up in Uptick: not changed or removed in FyreApp
        var setup = new FyreApp.Services.Routines.RoutineSetupService(db);
        Assert.Equal(FyreApp.Services.Routines.RoutineSetupService.FromUptickError, (await setup.RemoveAsync(service.Id)).Error);
        Assert.True((await setup.ListAsync(service.PropertyId)).Single().FromUptick);
    }

    [Fact]
    public async Task Products_warehouses_and_stock_are_mapped_and_replace_the_stock_snapshot()
    {
        await using var db = DbContextFactory.Create();
        db.Stocks.Add(new Stock { Warehouse = "Old", Product = "From the annotated stocks export" });
        db.Products.Add(new Product { Name = "Old name", UptickId = 704, LastUsedUtc = Now });
        db.UptickRecords.AddRange(
            RawUser("164", "Sam Nguyen (Field Tech)", "sam@fyrepower.com.au", "FIELD"),
            Raw("suppliers", "12", """{"name":"Wormald"}"""),
            Raw("products", "704", """{"name":"2.5kg ABE Extinguisher","type":"M","code":"FE25","sku":"W-25","cost_price":"40.0000","unit_price":"95.0000","is_active":true,"is_stocked":true}""",
                "{\"supplier\":" + To("Supplier", "12") + "}"),
            Raw("warehouses", "2", """{"name":"Sam Van","type":"VAN","is_active":true}""", "{\"manager\":" + To("User", "164") + ",\"branch\":{\"data\":null}}"),
            Raw("stock", "104", """{"amount_on_hand":"5.00","amount_reserved_outgoing":2,"amount_reserved_incoming":1,"minimum_restock_level":4,"bin_location":"Shelf 2"}""",
                "{\"product\":" + To("Product", "704") + ",\"warehouse\":" + To("Warehouse", "2") + "}"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var product = await db.Products.SingleAsync();
        Assert.Equal((704L, "2.5kg ABE Extinguisher", "Wormald", 95.0000m, true, Now),
            (product.UptickId!.Value, product.Name, product.Supplier, product.DefaultPrice!.Value, product.Stocked, product.LastUsedUtc!.Value));
        var warehouse = await db.Warehouses.SingleAsync();
        Assert.Equal(("Sam Van", "VAN", "Sam Nguyen (Field Tech)", 200m), (warehouse.Name, warehouse.Type, warehouse.Manager, warehouse.StockValue!.Value));

        var stock = await db.Stocks.SingleAsync();
        Assert.Equal((product.Id, warehouse.Id, "2.5kg ABE Extinguisher", "Sam Van", 5m, 3m, 1m, 1m, "Shelf 2"),
            (stock.ProductId!.Value, stock.WarehouseId!.Value, stock.Product, stock.Warehouse, stock.OnHand!.Value, stock.Available!.Value,
                stock.OnOrder!.Value, stock.Required!.Value, stock.BinLocation));
    }
}
