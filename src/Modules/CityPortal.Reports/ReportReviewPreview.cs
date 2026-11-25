namespace CityPortal.Reports;

public sealed record ReportReviewPreview(Guid Id, string Title, string? Text, string? PublicUrl, long? TargetVersion, bool CanHide,
    long? ThroughSequence, ContentReportResponse[] Reports, ReportDecisionResponse[] Decisions);
