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

    private static UptickMapping Mapping(AppDbContext db) => new(db,
        [new ClientMapper(db), new PropertyMapper(db), new ClientContactMapper(db), new PropertyContactMapper(db), new UserMapper(db),
            new AssetTypeMapper(db), new AssetTypeVariantMapper(db), new AssetMapper(db),
            new RoutineServiceTypeMapper(db), new RoutineServiceLevelMapper(db), new RemarkTypeMapper(db),
            new ProductMapper(db), new WarehouseMapper(db), new StockMapper(db), new RoutineServiceMapper(db), new RoutineServiceLevelLinkMapper(db), new TaskMapper(db), new ServiceTaskMapper(db),
            new DefectQuoteMapper(db), new ServiceQuoteMapper(db), new RemarkMapper(db),
            new ReportMapper(db), new RoutineMapper(db), new TimelineEventMapper(db)],
        NullLogger<UptickMapping>.Instance);

    // Every mapper in order, as a run ends
    private static async Task<Dictionary<string, UptickEndpointResult>> MapAllAsync(AppDbContext db, bool remap = false)
    {
        var mapping = Mapping(db);
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
        Assert.Equal((2806L, "2806", "Allied OT Pty Limited", true), (client.UptickId!.Value, client.ExternalId, client.Name, client.Active));
        Assert.Equal(("Belinda Boyd", "anita@alliedx.com.au", null, "Retail Trade"),
            (client.PrimaryContactName, client.PrimaryContactEmail, client.PrimaryContactPhone, client.PrimarySector));
        Assert.Equal(DateTimeOffset.Parse("2026-10-15T11:37:44.374802+11:00").UtcDateTime, client.Created);

        var property = await db.Properties.SingleAsync();
        Assert.Equal((1491L, "P-0016", "P-0016", client.Id), (property.UptickId!.Value, property.Ref, property.ExternalId, property.ClientId));
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
    public async Task Rows_a_file_import_made_are_updated_in_place_and_placeholders_filled_in()
    {
        await using var db = DbContextFactory.Create();
        var placeholder = new Client { Name = "Client 2806", ExternalId = "2806", IsPlaceholder = true, Active = false, ReportAddress = "PO Box 1" };
        db.Clients.Add(placeholder);
        db.Properties.Add(new Property { Name = "Old name", ExternalId = "P-0016", Client = placeholder });
        db.UptickUsers.Add(new UptickUser { Name = "Wes", ExternalId = "12", SecurityGroups = "Technician" });
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
            (asset.ExternalId, asset.Name, asset.Ref, asset.Location, asset.Quantity, asset.Compliance));
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
        var client = new Client { Name = "Allied", UptickId = 2806, ExternalId = "2806" };
        var property = new Property { Name = "Big River", UptickId = 1491, ExternalId = "P-0016", Client = client };
        // Made by name from the assets export, then a variant from the variants export, then the asset
        var type = new AssetType { Name = "fire extinguisher" };
        var variant = new AssetTypeVariant { Name = "CO2 5.0KG", AssetType = type };
        var asset = new Asset { Name = "Old", ExternalId = "42896", Property = property, AssetTypes = { type }, InternalNote = "x" };
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
        await MapAllAsync(db, remap: true);
        await ChangeInUptickAsync(db, "42896", "internal_note", "\"Swap at next service\"");
        await MapAllAsync(db);
        asset = await db.Assets.SingleAsync();
        Assert.Equal(("Plant room", "Swap at next service"), (asset.Location, asset.InternalNote));
        Assert.True((await db.LocalEdits.SingleAsync()).IsOpen);

        // Changed in Uptick since: Uptick's wins and the edit closes
        await ChangeInUptickAsync(db, "42896", "location", "\"Store room\"");
        await MapAllAsync(db);
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

        await MapAllAsync(db, remap: true);
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
    public async Task An_export_now_migrated_from_the_API_isnt_imported()
    {
        var service = new FyreApp.Services.Imports.UptickImportService([]);
        using var csv = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Ref,Name,Client ID,Address Street Address\nP-0016,Big River,2806,11 Greg Chappell Dr\n"));

        var result = await service.ImportAsync(csv, "properties.csv", null, dryRun: true);

        Assert.Equal("Properties come from Uptick's API now (Admin → Uptick migration), so their file import is off.", result.Error);
        using var again = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Label,Property Ref,Guid,Type,Variant\n"));
        Assert.StartsWith("Assets come from", (await service.ImportAsync(again, "x.csv", FyreApp.Services.Imports.UptickExportType.Assets, true)).Error);
    }

    [Fact]
    public async Task Routine_service_types_levels_and_remark_types_are_mapped_and_claim_what_the_exports_made()
    {
        await using var db = DbContextFactory.Create();
        // From the routines export (by name) and a remarks export (its row names the remark type's id)
        var made = new RoutineServiceType { Name = "10 - Portable and Wheeled Fire Extinguishers" };
        db.RoutineServiceTypes.Add(made);
        db.RoutineServiceLevels.Add(new RoutineServiceLevel { Name = "six-monthly", RoutineServiceType = made, IntervalMonths = 6 });
        var client = new Client { Name = "Allied", UptickId = 2806, ExternalId = "2806" };
        var property = new Property { Name = "Big River", UptickId = 1491, ExternalId = "P-0016", Client = client };
        db.AddRange(client, property, new Remark { Property = property, ExternalId = "500", UptickData = "{\"ID\":\"500\",\"Remark Type ID\":\"723\"}" });
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
        Assert.Equal(remarkType.Id, (await db.Remarks.SingleAsync()).RemarkTypeId);
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
            (task.Ref, task.ExternalId, task.PropertyId, task.ClientId, task.Category, task.Status, task.IsActive));
        Assert.Equal(("Wes Speedon (Field Tech)", "Wes Speedon (Field Tech)", "- 09 - Fire Hose Reels (Section 9): Annual"),
            (task.AssignedTo!.Name, task.UptickTechnician, task.ScopeOfWorks));
        Assert.Equal((new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 15, 4, 22, 53, DateTimeKind.Utc)),
            (task.DueDateUtc!.Value, task.CompletedUtc!.Value));

        // No property in Uptick: on the placeholder property, as the tasks export did
        var orphan = await db.ClientTasks.Include(t => t.Property).SingleAsync(t => t.UptickId == 46790);
        Assert.Equal((FyreApp.Services.Imports.Uptick.TaskImporter.NoPropertySiteName, ClientTaskStatus.Open), (orphan.Property.Name, orphan.Status));

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
    public async Task A_task_the_tasks_export_placed_is_updated_and_keeps_its_export_only_columns()
    {
        await using var db = DbContextFactory.Create();
        var client = new Client { Name = "Allied", UptickId = 2806, ExternalId = "2806" };
        var property = new Property { Name = "Big River", UptickId = 1491, ExternalId = "P-0016", Client = client };
        var task = new ClientTask { Client = client, Property = property, Title = "Old", ExternalId = "46781", UptickData = """{"Tags":"Urgent","Status":"READY"}""" };
        db.AddRange(client, property, task);
        db.UptickRecords.AddRange(RawClient("2806", "Allied"), RawProperty("1491", "P-0016", "2806"), RawTask("46781", "1491"));
        await db.SaveChangesAsync();

        await MapAllAsync(db);

        var mapped = await db.ClientTasks.SingleAsync();
        Assert.Equal((task.Id, 46781L, "PM2026/10 Servicing - Portables & Fire Equipment"), (mapped.Id, mapped.UptickId!.Value, mapped.Title));
        var fields = await FyreApp.Services.Tasks.TaskUptickFields.ForAsync(db, mapped, CancellationToken.None);
        Assert.Equal(("Urgent", "COMPLETE"), (fields["Tags"], fields["Status"]));
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
        Assert.Equal(("T-46781", "Q-2790", "APPROVED", "T-46900", "READY"),
            (remark.RaisedOnTaskRef, remark.QuoteRef, remark.QuoteStatus, remark.RepairTaskRef, remark.RepairTaskStatus));
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
        db.DefectQuotes.Add(new DefectQuote { Reference = "Q-0970", ExternalId = "999", PropertyRef = "P-0016", Total = 1m });
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
        db.Products.Add(new Product { Name = "Old name", ExternalId = "704", LastUsedUtc = Now });
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
