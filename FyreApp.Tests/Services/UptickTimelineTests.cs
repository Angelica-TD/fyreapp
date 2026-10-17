using System.Text.Json;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Timeline;
using FyreApp.Services.Uptick.Mapping;
using FyreApp.Services.Uptick.Migration;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FyreApp.Tests.Services;

// Uptick's timelineevents taken into TimelineEvent: its templates filled in as Uptick shows them, events linked to
// the property or asset FyreApp holds, notes, deletions and the Map link (docs/uptick-migration.md phase 2)
public class UptickTimelineTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Render(string template, string data = "{}", string links = "{}") =>
        TimelineText.Render(template, Json(data), Json(links));

    [Fact]
    public void A_tracked_change_reads_as_in_Uptick()
    {
        Assert.Equal("<em>Status</em> was changed to <em>INACTIVE</em>.",
            Render("<em>{verbose_name}</em> was changed to <em>{new_val}</em>.", """{"verbose_name":"Status","new_val":"INACTIVE"}"""));
    }

    [Fact]
    public void Values_are_written_as_Uptick_writes_them()
    {
        Assert.Equal("<em>Compliant</em> was changed to <em>None.</em> (3, True)",
            Render("<em>{field_name}</em> was changed to <em>{new_value}.</em> ({n}, {b})",
                """{"field_name":"Compliant","new_value":null,"n":3,"b":true}"""));
    }

    [Fact]
    public void A_link_to_another_record_shows_its_number_and_label()
    {
        Assert.Equal("Sent Dispatch <span title=\"24440: PM2025/10 Servicing\">#24440</span>.",
            Render("Sent Dispatch #Dispatch:{dispatch_pk}.", """{"dispatch_pk":24440}""",
                """{"Dispatch":{"24440":{"label":"24440: PM2025/10 Servicing ","web_url":"/dispatches/dispatches/24440/"}}}"""));
        Assert.Equal("Property #9999 was created.",
            Render("{model_name} #{model}:{pk} was created.", """{"model_name":"Property","model":"Property","pk":9999}"""));
    }

    [Fact]
    public void Everything_but_em_p_and_small_is_encoded()
    {
        Assert.Equal("<small>Unable to validate:</small><p></p> &lt;script&gt;x&lt;/script&gt; <em>&lt;b&gt;&amp;</em>",
            Render("<small>Unable to validate:</small><p></p> <script>x</script> <em>{v}</em>", """{"v":"<b>&"}"""));
    }

    [Fact]
    public void A_placeholder_with_no_value_is_left_as_written() =>
        Assert.Equal("Loss reason: {loss_reason}", Render("Loss reason: {loss_reason}"));

    // The raw copy as a Full run leaves it: content types, a property, and events on a property, an asset and a task
    private static async Task<AppDbContext> SeedAsync()
    {
        var db = DbContextFactory.Create();
        db.UptickRecords.AddRange(
            Raw("contenttypes", "19", """{"model":"property","app_label":"properties"}"""),
            Raw("contenttypes", "51", """{"model":"asset","app_label":"assets"}"""),
            Raw("contenttypes", "28", """{"model":"task","app_label":"tasks"}"""),
            Raw("properties", "9999", """{"ref":"P-3340","name":"Gold Coast Concrete"}"""),
            Event(1, "19", 9999, "trackedchange", "E", "<em>{verbose_name}</em> was changed to <em>{new_val}</em>.",
                """{"verbose_name":"Access note","new_val":"Please send reminder email"}""", "2025-12-02T16:08:25.309845+11:00"),
            Event(2, "51", 41642, "general", "C", "Gate code is 1234\nAsk at reception", "{}", "2026-10-15T15:15:20+11:00",
                lat: -27.95, lng: 153.4),
            Event(3, "28", 31713, "tasks", "E", "Task created.", "{}", "2026-01-01T09:00:00+10:00"));
        db.Sites.Add(new Site { Name = "Gold Coast Concrete", ExternalId = "P-3340", Client = new Client { Name = "Jane Clandy" } });
        await db.SaveChangesAsync();
        return db;
    }

    private static UptickRecord Raw(string resource, string id, string attributes, string relationships = "{}") => new()
    {
        Resource = resource, UptickId = id, Attributes = attributes, Relationships = relationships, Hash = id,
        FetchedUtc = Now, ChangedUtc = Now, SeenUtc = Now
    };

    private static UptickRecord Event(int id, string contentType, long objectId, string key, string type, string text, string data,
        string created, double? lat = null, double? lng = null)
    {
        var attributes = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["key"] = key, ["type"] = type, ["text"] = text, ["data"] = Json(data), ["data_links"] = Json("{}"),
            ["created"] = created, ["object_id"] = objectId, ["author_name"] = "Wes Speedon (Field Tech)",
            ["coord_lat"] = lat, ["coord_lng"] = lng, ["is_deleted"] = false
        });
        var record = Raw("timelineevents", id.ToString(), attributes,
            "{\"account\":{\"data\":null},\"content_type\":{\"data\":{\"id\":\"" + contentType + "\",\"type\":\"ContentType\"}}}");
        record.CreatedUtc = DateTimeOffset.Parse(created).UtcDateTime;
        return record;
    }

    private static UptickMapping Mapping(AppDbContext db) =>
        new(db, [new TimelineEventMapper(db)], NullLogger<UptickMapping>.Instance);

    private static async Task<UptickEndpointResult> MapAsync(AppDbContext db, bool remap = false)
    {
        var result = new UptickEndpointResult { Resource = "timelineevents" };
        await Mapping(db).MapAsync("timelineevents", remap, result, null, CancellationToken.None);
        Assert.Null(result.MapError);
        return result;
    }

    [Fact]
    public async Task Every_event_is_mapped_and_linked_to_the_property_FyreApp_holds()
    {
        await using var db = await SeedAsync();

        Assert.Equal(3, (await MapAsync(db)).Mapped);

        var site = await db.Sites.SingleAsync();
        var change = Assert.Single(await new TimelineService(db).ForPropertyAsync(site, notesOnly: false));
        Assert.Equal("<em>Access note</em> was changed to <em>Please send reminder email</em>.", change.Html);
        Assert.Equal(("trackedchange", "Wes Speedon (Field Tech)", false), (change.Key, change.Author, change.IsNote));
        Assert.Equal(new DateTime(2025, 12, 2, 5, 8, 25, 309, DateTimeKind.Utc).AddTicks(8450), change.WhenUtc);

        // The task's event is kept for when FyreApp shows a task's Timeline
        var task = await db.TimelineEvents.SingleAsync(e => e.UptickId == 3);
        Assert.Equal(("task", 31713L, (int?)null, (int?)null), (task.UptickObjectType, task.UptickObjectId!.Value, task.SiteId, task.AssetId));
        Assert.All(await db.UptickRecords.Where(r => r.Resource == "timelineevents").ToListAsync(), r => Assert.NotNull(r.MappedUtc));
    }

    [Fact]
    public async Task A_note_keeps_its_text_and_where_it_was_written_and_reaches_an_asset_held_later()
    {
        await using var db = await SeedAsync();
        await MapAsync(db);
        Assert.Null((await db.TimelineEvents.SingleAsync(e => e.UptickId == 2)).AssetId);

        // The asset comes in afterwards (a file import); the next mapping pass links its events, with nothing waiting
        var asset = new Asset { Name = "001 - DCP", ExternalId = "41642", SiteId = (await db.Sites.SingleAsync()).Id };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        Assert.Equal(0, (await MapAsync(db)).Mapped);

        var note = Assert.Single(await new TimelineService(db).ForAssetAsync(asset, notesOnly: true));
        Assert.True(note.IsNote);
        Assert.Equal("Gate code is 1234\nAsk at reception", note.Html);
        Assert.Equal((-27.95, 153.4), (note.Latitude!.Value, note.Longitude!.Value));
    }

    [Fact]
    public async Task A_remap_updates_in_place_and_an_event_deleted_in_Uptick_leaves_the_timeline()
    {
        await using var db = await SeedAsync();
        await MapAsync(db);
        var firstIds = await db.TimelineEvents.OrderBy(e => e.UptickId).Select(e => e.Id).ToListAsync();

        var gone = await db.UptickRecords.SingleAsync(r => r.Resource == "timelineevents" && r.UptickId == "3");
        gone.DeletedUtc = Now;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(3, (await MapAsync(db, remap: true)).Mapped);
        Assert.Equal(firstIds.Take(2), await db.TimelineEvents.OrderBy(e => e.UptickId).Select(e => e.Id).ToListAsync());
        Assert.False(await db.TimelineEvents.AnyAsync(e => e.UptickId == 3));
    }

    [Fact]
    public async Task A_record_that_cant_be_read_is_reported_and_the_rest_are_mapped()
    {
        await using var db = await SeedAsync();
        db.UptickRecords.Add(Raw("timelineevents", "4", "not json"));
        await db.SaveChangesAsync();

        var result = await MapAsync(db);

        Assert.Equal((3, 1), (result.Mapped!.Value, result.MapFailed));
        Assert.StartsWith("4: ", Assert.Single(result.MapErrors));
        Assert.Null((await db.UptickRecords.SingleAsync(r => r.Resource == "timelineevents" && r.UptickId == "4")).MappedUtc);
    }

    [Fact]
    public async Task A_remap_run_maps_without_asking_Uptick_and_reports_it()
    {
        await using var db = await SeedAsync();
        db.UptickEndpointStates.Add(new UptickEndpointState { Resource = "timelineevents", CopiedCount = 3 });
        await db.SaveChangesAsync();
        var api = new Moq.Mock<FyreApp.Services.Uptick.Api.IUptickApiClient>(Moq.MockBehavior.Loose);

        var run = await new UptickMigrator(db, api.Object, NullLogger<UptickMigrator>.Instance, mapping: Mapping(db))
            .RunAsync(UptickRunKind.Remap, null, "admin");

        Assert.Equal("Completed", run.Status);
        var result = Assert.Single(JsonSerializer.Deserialize<List<UptickEndpointResult>>(run.Report!)!);
        Assert.Equal(("timelineevents", "Ok", "Map", 3), (result.Resource, result.Status, result.Mode, result.Mapped!.Value));
        Assert.Equal(3, (await db.UptickEndpointStates.SingleAsync()).MappedCount);
        api.Verify(a => a.ListEndpointsAsync(Moq.It.IsAny<CancellationToken>()), Moq.Times.Never);
        Assert.Equal(3, await db.TimelineEvents.CountAsync());
    }
}
