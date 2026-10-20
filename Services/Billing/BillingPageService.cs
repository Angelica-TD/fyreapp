using FyreApp.Data;
using FyreApp.Services.Uptick.Files;
using FyreApp.ViewModels.Billing;
using Microsoft.EntityFrameworkCore;

namespace FyreApp.Services.Billing;

public interface IBillingPageService
{
    Task<InvoicePageVm?> InvoiceAsync(int id, CancellationToken ct = default);
    Task<DefectQuotePageVm?> QuoteAsync(int id, CancellationToken ct = default);
    Task<ServiceQuotePageVm?> SalesQuoteAsync(int id, CancellationToken ct = default);
}

// Uptick's invoices, defect quotes and sales quotes, each with its lines (in Uptick's order) and its PDF
public class BillingPageService : IBillingPageService
{
    private readonly AppDbContext _db;
    public BillingPageService(AppDbContext db) => _db = db;

    private async Task<int?> PdfAsync(string resource, long? uptickId, CancellationToken ct) =>
        (await UptickFileLinks.PdfsAsync(_db, resource, [uptickId], ct)).Values.Cast<int?>().FirstOrDefault();

    public async Task<InvoicePageVm?> InvoiceAsync(int id, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Property!).ThenInclude(p => p.Client)
            .Include(i => i.ClientTask)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice == null) return null;
        return new InvoicePageVm
        {
            Invoice = invoice,
            Lines = await _db.InvoiceLines.AsNoTracking().Include(l => l.Product)
                .Where(l => l.InvoiceId == id).OrderBy(l => l.Index).ThenBy(l => l.Id).ToListAsync(ct),
            CreditNotes = await _db.CreditNotes.AsNoTracking().Where(c => c.InvoiceId == id).OrderBy(c => c.Date).ToListAsync(ct),
            PdfFileId = await PdfAsync(UptickResources.Invoices, invoice.UptickId, ct)
        };
    }

    public async Task<DefectQuotePageVm?> QuoteAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.DefectQuotes.AsNoTracking()
            .Include(q => q.Property!).ThenInclude(p => p.Client)
            .Include(q => q.ClientTask)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null) return null;
        var lines = await _db.DefectQuoteLines.AsNoTracking()
            .Include(l => l.Product).Include(l => l.Asset).Include(l => l.Remark)
            .Where(l => l.DefectQuoteId == id).OrderBy(l => l.Index).ThenBy(l => l.Id).ToListAsync(ct);
        var remarkIds = lines.Select(l => l.RemarkId).OfType<int>().ToList();
        return new DefectQuotePageVm
        {
            Quote = quote,
            Lines = lines,
            Remarks = await _db.Remarks.AsNoTracking().Include(r => r.Asset)
                .Where(r => r.DefectQuoteId == id || remarkIds.Contains(r.Id))
                .OrderBy(r => r.UptickId).ToListAsync(ct),
            PdfFileId = await PdfAsync(UptickResources.DefectQuotes, quote.UptickId, ct)
        };
    }

    public async Task<ServiceQuotePageVm?> SalesQuoteAsync(int id, CancellationToken ct = default)
    {
        var quote = await _db.ServiceQuotes.AsNoTracking()
            .Include(q => q.Property!).ThenInclude(p => p.Client)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null) return null;
        return new ServiceQuotePageVm
        {
            Quote = quote,
            Lines = await _db.ServiceQuoteLines.AsNoTracking().Include(l => l.Product).Include(l => l.RoutineServiceType)
                .Where(l => l.ServiceQuoteId == id)
                // Routine services first, as Uptick's quote lists them, then products
                .OrderBy(l => l.Kind == "product").ThenBy(l => l.Index).ThenBy(l => l.Id).ToListAsync(ct),
            PdfFileId = await PdfAsync(UptickResources.ServiceQuotes, quote.UptickId, ct)
        };
    }
}
