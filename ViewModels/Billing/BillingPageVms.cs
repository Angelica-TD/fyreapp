using FyreApp.Models;

namespace FyreApp.ViewModels.Billing;

// The pages of Uptick's invoices, defect quotes and sales quotes: the record, its lines and its PDF

public class InvoicePageVm
{
    public Invoice Invoice { get; init; } = null!;
    public IReadOnlyList<InvoiceLine> Lines { get; init; } = [];
    public IReadOnlyList<CreditNote> CreditNotes { get; init; } = [];
    public int? PdfFileId { get; init; }
}

public class DefectQuotePageVm
{
    public DefectQuote Quote { get; init; } = null!;
    public IReadOnlyList<DefectQuoteLine> Lines { get; init; } = [];

    // The remarks it quotes for (each remark's latest quote)
    public IReadOnlyList<Remark> Remarks { get; init; } = [];
    public int? PdfFileId { get; init; }
}

public class ServiceQuotePageVm
{
    public ServiceQuote Quote { get; init; } = null!;
    public IReadOnlyList<ServiceQuoteLine> Lines { get; init; } = [];
    public int? PdfFileId { get; init; }
}
