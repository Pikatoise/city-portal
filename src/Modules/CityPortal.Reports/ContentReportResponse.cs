namespace CityPortal.Reports;

public sealed record ContentReportResponse(long Sequence, string Category, string? Text, DateTimeOffset ReceivedAtUtc, Guid? DecisionId);
