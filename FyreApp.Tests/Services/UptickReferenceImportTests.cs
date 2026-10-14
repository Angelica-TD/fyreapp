using System.Text;
using FyreApp.Data;
using FyreApp.Models;
using FyreApp.Services.Imports;
using FyreApp.Services.Imports.Uptick;
using FyreApp.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FyreApp.Tests.Services;

// Uptick's groups, extra fields and prompt questions exports, and using them on the Users, asset and property pages
public class UptickReferenceImportTests
{
    private static Stream Csv(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    // Export type left to detection, as when an admin just drops the file in
    private static Task<FyreApp.ViewModels.Imports.UptickImportResultVm> Import(AppDbContext db, Stream csv) =>
        new UptickImportService(new UptickImporter[]
        {
            new AssetTypeImporter(db), new RoutineServiceTypeImporter(db), new UserImporter(db),
            new SecurityGroupImporter(db), new ExtraFieldImporter(db), new PromptQuestionImporter(db)
        }).ImportAsync(csv, "export.csv", null, dryRun: false);

    private const string PromptHeader = "\"ID\",\"Created\",\"Updated\",\"Promptset Name\",\"Promptset Ref\",\"Promptset Type\",\"Promptset Assettypes\",\"Promptset Routineservicetypes\",\"Type\",\"Label\",\"Help Text\",\"Ref\",\"Active\",\"Required\",\"Order\",\"Config\",\"Remark Types\",\"Limit to Variants\",\"Limit to Levels\",\"Rules\",\"Choice Keys\",\"Choice Values\"";

    [Fact]
    public async Task Groups_AreOnlyAFileWithJustIdAndName_AndRefreshOnRerun()
    {
        using var db = DbContextFactory.Create();

        var first = await Import(db, Csv("\"id\",\"name\"", "1,\"Contractor\"", "8,\"Technician - Higher Permissions\""));
        Assert.Equal(((UptickExportType?)UptickExportType.SecurityGroups, 2), (first.Type, first.Created));

        var second = await Import(db, Csv("\"id\",\"name\"", "8,\"Technician (higher permissions)\""));
        Assert.Equal((0, 1), (second.Created, second.Updated));
        Assert.Equal("Technician (higher permissions)", (await db.SecurityGroups.SingleAsync(g => g.ExternalId == "8")).Name);

        // Another export with id and name plus more columns isn't taken for groups
        var other = await Import(db, Csv("\"id\",\"name\",\"colour\"", "1,\"Red\",\"#f00\""));
        Assert.Null(other.Type);
        Assert.NotNull(other.Error);
    }

    [Fact]
    public async Task ExtraFields_Import_WithTheirRecordType()
    {
        using var db = DbContextFactory.Create();

        var result = await Import(db, Csv(
            "\"id\",\"content_type\",\"order\",\"label\",\"name\",\"field_settings\",\"widget\",\"default\",\"is_required\",\"help_text\"",
            "5,\"51\",1,\"ASE Connected\",\"ase_connected\",\"\",\"choice\",\"TBA\",False,\"Connected to alarm monitoring devise?\"",
            "20,\"19\",2,\"Mains Pressure (kPa)\",\"mains_pressure\",\"\",\"text\",\"\",True,\"\""));

        Assert.Equal(((UptickExportType?)UptickExportType.ExtraFields, 2), (result.Type, result.Created));
        var ase = await db.ExtraFields.SingleAsync(f => f.ExternalId == "5");
        Assert.Equal(("Asset", "choice", "TBA", false), (ase.RecordType, ase.Widget, ase.Default, ase.IsRequired));
        var mains = await db.ExtraFields.SingleAsync(f => f.ExternalId == "20");
        Assert.Equal(("Property", true), (mains.RecordType, mains.IsRequired));
    }

    [Fact]
    public async Task PromptQuestions_AnExportWithNoRowsIsStillRecognised()
    {
        using var db = DbContextFactory.Create();

        var empty = await Import(db, Csv(PromptHeader));
        Assert.Equal(((UptickExportType?)UptickExportType.PromptQuestions, (string?)null, 0), (empty.Type, empty.Error, empty.TotalRows));
        Assert.Contains(empty.Notes, n => n.Contains("no questions"));

        var withRows = await Import(db, Csv(PromptHeader,
            "7,\"2025-01-01 09:00:00\",\"2025-01-01 09:00:00\",\"Extinguisher checks\",\"PS-1\",\"asset\",\"\",\"\",\"choice\",\"Pressure gauge in green?\",\"\",\"Q1\",True,True,1,\"\",\"\",\"\",\"\",\"\",\"yes,no\",\"Yes,No\""));
        var question = await db.PromptQuestions.SingleAsync();
        Assert.Equal((1, "Pressure gauge in green?", "Extinguisher checks", true, 1), (withRows.Created, question.Label, question.PromptsetName, question.Required, question.Order));
    }

    [Theory]
    [InlineData("Technician - Technician - Higher Permissions", new[] { "Technician", "Technician - Higher Permissions" })]
    [InlineData("Office - Manager", new[] { "Office", "Manager" })]
    [InlineData("Client", new[] { "Client" })]
    [InlineData("Unknown - Office", new[] { "Unknown", "Office" })]
    public void SecurityGroups_SplitOnTheLongestKnownName(string joined, string[] expected) =>
        Assert.Equal(expected, UptickReferenceValues.SplitSecurityGroups(joined,
            new[] { "Technician", "Technician - Higher Permissions", "Office", "Manager", "Client" }));

    [Fact]
    public void ExtraFields_ValuesFromTheRecordsUptickRow_InUpticksOrder_BlanksLeftOut()
    {
        var fields = new[]
        {
            new ExtraField { Label = "TAG No.", Order = 2 },
            new ExtraField { Label = "ASE Connected", Order = 1, HelpText = "Connected to alarm monitoring devise?" },
            new ExtraField { Label = "Actuator type", Order = 3 }
        }.OrderBy(f => f.Order);

        var values = UptickReferenceValues.ExtraFields(fields, """{"Label":"Pump 1","ASE Connected":"Yes","TAG No.":"T-17","Actuator type":""}""");

        Assert.Equal(new[] { ("ASE Connected", "Yes"), ("TAG No.", "T-17") }, values.Select(v => (v.Label, v.Value)));
        Assert.Equal("Connected to alarm monitoring devise?", values[0].HelpText);
    }

    [Fact]
    public async Task UsersList_ShowsSplitGroups()
    {
        using var db = DbContextFactory.Create();
        db.SecurityGroups.AddRange(new SecurityGroup { Name = "Technician" }, new SecurityGroup { Name = "Technician - Higher Permissions" });
        db.UptickUsers.Add(new UptickUser { Name = "Dwayne Page", LicenseType = "FIELD", SecurityGroups = "Technician - Technician - Higher Permissions" });
        await db.SaveChangesAsync();

        var list = await new FyreApp.Services.Lists.DirectoryListService(db).UptickUsersAsync(new FyreApp.ViewModels.Lists.DirectoryFilter { Active = "any" });

        Assert.Equal(new[] { "Technician", "Technician - Higher Permissions" }, Assert.Single(list.Items).Groups);
    }
}
