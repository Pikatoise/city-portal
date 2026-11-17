namespace CityPortal.News;

/// <summary>A bounded queue item; content and history require assignment.</summary>
public sealed record ReviewSummaryResponse(Guid Id, long Version, string Title, string State, Guid? ReviewerId, DateTimeOffset UpdatedAtUtc);
