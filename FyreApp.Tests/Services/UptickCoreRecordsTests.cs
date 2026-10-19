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
// phase 3b): linked by Uptick id, rows from earlier file imports updated in place, deletions deactivating
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
            new TimelineEventMapper(db)],
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
}
