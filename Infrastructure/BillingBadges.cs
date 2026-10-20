using System.Globalization;

namespace FyreApp.Infrastructure;

// How Uptick's quote and invoice statuses and amounts show
public static class BillingBadges
{
    public static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-AU");

    public static string Amount(decimal? value) => value?.ToString("C", Money) ?? "-";

    public static string Quantity(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Quote(string? status) => status switch
    {
        "ACTIONED" or "APPROVED" or "COMPLETED" or "FINALISED" => "bg-success",
        "SUBMITTED" => "bg-primary",
        "DRAFT" => "bg-light text-dark border",
        "DECLINED" => "bg-danger",
        _ => "bg-secondary"
    };

    public static string Invoice(string? status) => status switch
    {
        "PAID" => "bg-success",
        "AUTHORISED" => "bg-primary",
        "VOIDED" or "DELETED" => "bg-secondary",
        "DRAFT" or "SUBMITTED" => "bg-light text-dark border",
        _ => "bg-secondary"
    };
}
