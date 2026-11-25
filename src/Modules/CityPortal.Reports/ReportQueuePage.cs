namespace CityPortal.Reports;

public sealed record ReportQueuePage(ReportedContentSummary[] Items, bool HasNextPage);
