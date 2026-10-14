using FyreApp.Data;
using FyreApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Imports.Uptick;

// Uptick "prompt questions" export -> PromptQuestion (reference data, refreshed from each export): the
// questions in Uptick's inspection promptsets
public class PromptQuestionImporter : UptickImporter
{
    public PromptQuestionImporter(AppDbContext db) : base(db) { }

    public override UptickExportType Type => UptickExportType.PromptQuestions;
    public override string DisplayName => "Prompt questions";
    public override string[] SignatureHeaders => new[] { "Promptset Name", "Promptset Ref", "Choice Keys" };

    public override async Task ImportAsync(IReadOnlyList<ImportRow> rows, bool dryRun, ImportContext ctx, CancellationToken ct)
    {
        var byExt = (await Db.PromptQuestions.Where(q => q.ExternalId != null).ToListAsync(ct))
            .ToDictionary(q => q.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0;

        foreach (var row in rows)
        {
            var id = row.Get("ID");
            if (Claim(id, NoneImported, seen, row, ctx, out _) == RowAction.Skip) continue;

            if (!byExt.TryGetValue(id!, out var question))
            {
                question = new PromptQuestion { ExternalId = id };
                byExt[id!] = question;
                if (!dryRun) Db.PromptQuestions.Add(question);
                created++;
            }
            else updated++;

            if (dryRun) continue;

            question.PromptsetName = Cap(row.Get("Promptset Name"), 200);
            question.PromptsetRef = Cap(row.Get("Promptset Ref"), 100);
            question.PromptsetType = Cap(row.Get("Promptset Type"), 100);
            question.Type = Cap(row.Get("Type"), 100);
            question.Label = Cap(row.Get("Label"), 500) ?? $"Question {id}";
            question.HelpText = row.Get("Help Text");
            question.Ref = Cap(row.Get("Ref"), 100);
            question.Active = row.GetBool("Active") ?? true;
            question.Required = row.GetBool("Required") ?? false;
            question.Order = row.GetInt("Order");
            question.UptickData = row.ToJson();
        }

        ctx.Result.Created = created;
        NoteUpdated(ctx, updated, dryRun, "prompt questions");
        if (rows.Count == 0)
            ctx.Result.Notes.Add("The export has no questions (Uptick exported the column headers only).");
        if (!dryRun) await Db.SaveChangesAsync(ct);
    }
}
