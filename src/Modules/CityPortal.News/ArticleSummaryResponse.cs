namespace CityPortal.News;

/// <summary>A summary from the current author's workspace.</summary>
public sealed record ArticleSummaryResponse(Guid Id, long Version, string Title, string State, DateTimeOffset UpdatedAtUtc);
