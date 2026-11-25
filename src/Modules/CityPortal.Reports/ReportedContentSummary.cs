namespace CityPortal.Reports;

public sealed record ReportedContentSummary(Guid Id, string Title, int Total, int Pending);
