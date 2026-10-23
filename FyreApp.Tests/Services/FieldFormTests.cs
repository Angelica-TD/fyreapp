using System.Text.Json;
using System.Text.Json.Nodes;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Field;
using FyreApp.Services.Files;
using FyreApp.Services.Forms;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace FyreApp.Tests.Services;

// Forms filled in on a task in the field app, from Uptick's form templates, and the customer's sign-off
public class FieldFormTests
{
    private const string Take5 = """
        fields:
            customer-name:
                type: text
                label: Customer name
                initial: "{{task.property.client.name}}"
                required: true
            task-number:
                type: text
                initial: "{{task.pk}}"
            section-1:
                type: message
                heading: 1. THINK about the task
            understand:
                type: assertion
                test: Do you understand the task?
                fail_prompt: What part of the task is not understood?
                required: true
                show_na: true
                pass_option: Yes
                fail_option: No
            ppe:
                type: checkbox
                label: PPE worn
            hazard-level:
                type: checkboxgroup
                options:
                  - value: high
                    label: High
                  - value: low
                    label: Low
            signature:
                type: signature
                label: Sign here
            technician-name:
                type: text
                initial: '{user_name}'
        layout:
            - type: group
              class: row
              contents:
                - type: text
                  class: h4 col-sm-12
                  text: "Take 5"
                - type: field
                  class: col-sm-6
                  field: customer-name
        """;

    // A 1x1 PNG
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private sealed record World(AppDbContext Db, ClientTask Task, UptickUser Me);

    private static async Task<World> SetupAsync()
    {
        var db = DbContextFactory.Create();
        var me = new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD" };
        var site = new Property { Name = "Gemvale Glades", AddressDisplay = "1 Gemvale Rd", Client = new Client { Name = "Gemvale CTS 38276" } };
        var task = new ClientTask { UptickId = 46748, Property = site, Client = site.Client, Title = "Six-monthly I&T", Ref = "T-46748", AssignedTo = me };
        db.AddRange(task);
        db.UptickRecords.AddRange(
            Raw("dynamicformtemplates", "1000", """{"name":"Take 5"}"""),
            Raw("dynamicformtemplateversions", "1000", JsonSerializer.Serialize(new { config = "fields:\n  old:\n    type: text\n" }), """{"template":{"data":{"id":"1000","type":"FormTemplate"}}}"""),
            Raw("dynamicformtemplateversions", "1003", JsonSerializer.Serialize(new { config = Take5 }), """{"template":{"data":{"id":"1000","type":"FormTemplate"}}}"""));
        await db.SaveChangesAsync();
        return new World(db, task, me);
    }

    private static UptickRecord Raw(string resource, string id, string attributes, string? relationships = null) =>
        new() { Resource = resource, UptickId = id, Hash = "h", Attributes = attributes, Relationships = relationships ?? "{}" };

    private static Dictionary<string, StringValues> Posted(params (string Key, string[] Values)[] values) =>
        values.ToDictionary(v => v.Key, v => new StringValues(v.Values));

    [Fact]
    public void ATemplate_IsReadInOrder_WithItsLayout()
    {
        var form = FormDefinition.Parse(Take5);

        Assert.Equal(new[] { "customer-name", "task-number", "section-1", "understand", "ppe", "hazard-level", "signature", "technician-name" },
            form.Fields.Select(f => f.Key));
        var understand = form.Field("understand")!;
        Assert.Equal(("assertion", "Yes", "No", true, true), (understand.Type, understand.PassOption, understand.FailOption, understand.ShowNa, understand.Required));
        Assert.Equal(new[] { ("high", "High"), ("low", "Low") }, form.Field("hazard-level")!.Options);
        Assert.Equal("Task number", form.Field("task-number")!.Caption);
        Assert.False(form.Field("section-1")!.TakesValue);

        var row = Assert.Single(form.Layout);
        Assert.Equal(("group", "row", 2), (row.Type, row.Class, row.Contents.Count));
        Assert.Equal("customer-name", row.Contents[1].Field);
        Assert.DoesNotContain(form.Unplaced(), f => f.Key == "customer-name");
        Assert.Contains(form.Unplaced(), f => f.Key == "understand");
    }

    [Fact]
    public void ABrokenTemplate_HasNoFields() =>
        Assert.Empty(FormDefinition.Parse("fields: [unclosed").Fields);

    [Fact]
    public async Task EachTemplatesLatestVersion_IsOffered_AndANewForm_IsFilledFromTheTask()
    {
        var w = await SetupAsync();
        var forms = new FieldFormService(w.Db);

        var template = Assert.Single(await forms.TemplatesAsync());
        Assert.Equal(("1003", "Take 5"), (template.VersionId, template.Name));

        var vm = (await forms.NewAsync(w.Task.Id, "1003", w.Me.Id))!;
        Assert.Equal(("Gemvale CTS 38276", "46748", "Dwayne Page"), (vm.Text("customer-name"), vm.Text("task-number"), vm.Text("technician-name")));
        Assert.Null(await forms.NewAsync(w.Task.Id, "9999", w.Me.Id));
    }

    [Fact]
    public async Task ASubmittedForm_KeepsItsAnswersAsUptickDoes()
    {
        var w = await SetupAsync();
        var forms = new FieldFormService(w.Db);

        var missing = await forms.SubmitAsync(w.Task.Id, w.Me.Id, "1003", Posted(("customer-name", ["Gemvale"])));
        Assert.Equal(FieldOutcome.Invalid, missing.Result.Outcome);
        Assert.Equal(new[] { "Do you understand the task?" }, missing.Missing);
        Assert.Equal("Gemvale", missing.Values!["customer-name"]!.GetValue<string>());
        Assert.Empty(w.Db.DynamicFormResponses);

        var saved = await forms.SubmitAsync(w.Task.Id, w.Me.Id, "1003", Posted(
            ("customer-name", ["Gemvale"]), ("understand", ["fail"]), ("understand__comment", ["Scope unclear"]),
            ("ppe", ["true"]), ("hazard-level", ["low", "made-up"]), ("signature", [Png]), ("__RequestVerificationToken", ["x"])));

        Assert.Equal(FieldOutcome.Done, saved.Result.Outcome);
        var response = await w.Db.DynamicFormResponses.SingleAsync();
        Assert.Equal(("Take 5", "1003", "46748", w.Me.Id, true), (response.TemplateName, response.TemplateVersion, response.TargetObjectId, response.UptickUserId!.Value, response.IsComplete));
        var values = JsonNode.Parse(response.Values!)!.AsObject();
        Assert.Equal("fail", values["understand"]!.GetValue<string>());
        Assert.Equal("Scope unclear", values["understand__comment"]!.GetValue<string>());
        Assert.True(values["ppe"]!.GetValue<bool>());
        Assert.Equal(new[] { "low" }, values["hazard-level"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(Png, values["signature"]!.GetValue<string>());
        Assert.False(values.ContainsKey("section-1"));
        Assert.False(values.ContainsKey("__RequestVerificationToken"));

        var view = (await forms.ViewAsync(w.Task.Id, response.Id))!;
        Assert.True(view.ReadOnly);
        Assert.True(view.Chosen("hazard-level", "low"));
        Assert.Equal("Dwayne Page", view.SubmittedBy);
        Assert.Equal("Take 5", Assert.Single(await forms.TaskFormsAsync(w.Task.Id)).Name);
    }

    [Fact]
    public async Task AMigratedForm_ShowsItsAnswersFromTheRawCopy()
    {
        var w = await SetupAsync();
        w.Db.DynamicFormResponses.Add(new DynamicFormResponse { UptickId = 5, ClientTask = w.Task, TemplateName = "Take 5", TemplateVersion = "1003", Ref = "F-00005", IsComplete = true });
        w.Db.UptickRecords.Add(Raw("dynamicformresponses", "5", """{"ref":"F-00005","values":{"customer-name":"From Uptick","ppe":true}}"""));
        await w.Db.SaveChangesAsync();

        var view = (await new FieldFormService(w.Db).ViewAsync(w.Task.Id, (await w.Db.DynamicFormResponses.SingleAsync()).Id))!;

        Assert.Equal("From Uptick", view.Text("customer-name"));
        Assert.True(view.Checked("ppe"));
        Assert.Equal("F-00005", view.Ref);
    }

    private sealed class MemoryStore : IFileStore
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public bool IsConfigured => true;
        public string Description => "memory";

        public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Files[key] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var f) ? new MemoryStream(f) : null);
    }

    [Fact]
    public async Task TheCustomersSignOff_IsASignatureWithTheirName_OrAReason()
    {
        var w = await SetupAsync();
        var store = new MemoryStore();
        var signOffs = new FieldSignOffService(w.Db, store);

        Assert.Equal(FieldOutcome.Invalid, (await signOffs.SignAsync(w.Task.Id, w.Me.Id, null, null, null)).Outcome);
        Assert.Equal(FieldOutcome.Invalid, (await signOffs.SignAsync(w.Task.Id, w.Me.Id, null, Png, null)).Outcome);
        Assert.Equal(FieldOutcome.Invalid, (await signOffs.SignAsync(w.Task.Id, w.Me.Id, "Faith", "data:image/png;base64,bm90IGEgcG5n", null)).Outcome);

        Assert.Equal(FieldOutcome.Done, (await signOffs.SignAsync(w.Task.Id, w.Me.Id, " Faith ", Png, null)).Outcome);
        var signed = (await signOffs.GetAsync(w.Task.Id))!;
        Assert.Equal(("Faith", (string?)null), (signed.SigneeName, signed.NoSignatureReason));
        var file = await w.Db.UptickFiles.SingleAsync();
        Assert.Equal(("signature", "Faith", "tasks", w.Task.Id), (file.Kind, file.Caption, file.RecordType, file.RecordId!.Value));
        Assert.True(store.Files.ContainsKey(file.StorageKey!));

        // Again, with nobody to sign: replaces the first
        Assert.Equal(FieldOutcome.Done, (await signOffs.SignAsync(w.Task.Id, w.Me.Id, null, null, "Nobody on site")).Outcome);
        var again = await w.Db.TaskSignOffs.SingleAsync();
        Assert.Equal(("Nobody on site", (int?)null), (again.NoSignatureReason, again.SignatureFileId));
        Assert.Empty(w.Db.UptickFiles);
    }

    [Fact]
    public void Placeholders_ComeFromTheTask_AndUnknownOnesAreLeftEmpty()
    {
        var task = new ClientTask { Id = 9, Title = "Repair", Property = new Property { Name = "Ezfurn", AddressDisplay = "2 Main St" }, Client = new Client { Name = "Ezfurn Pty" } };

        Assert.Equal("Ezfurn at 2 Main St", FieldFormService.Initial("{{task.property.name}} at {{ task.property.address.display }}", task, null));
        Assert.Equal("9", FieldFormService.Initial("{{task.pk}}", task, null));
        Assert.Equal("", FieldFormService.Initial("{{task.client.primary_contact.email}}", task, null));
        Assert.Equal("FYREPOWER FIRE PROTECTION", FieldFormService.Initial("FYREPOWER FIRE PROTECTION", task, null));
        Assert.Matches(@"^\d{2}/\d{2}/\d{4}$", FieldFormService.Initial("{now_date}", task, null));
    }
}
