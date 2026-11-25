namespace CityPortal.Reports;

public sealed record ReportTarget(Guid OwnerId, string Title, string Text, string? PublicUrl, long Version, bool CanHide);
