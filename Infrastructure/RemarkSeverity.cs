namespace FyreApp.Infrastructure;

// Uptick's remark severities: higher is worse. 10 critical defect, 5 non-critical defect, 2 non-conformance,
// 1 recommendation, 0 informational.
public static class RemarkSeverity
{
    public static string BadgeCss(int? severity) => severity switch
    {
        >= 10 => "bg-danger",
        >= 5 => "bg-orange text-white",
        >= 2 => "bg-warning text-dark",
        1 => "bg-info text-dark",
        _ => "bg-secondary"
    };
}
