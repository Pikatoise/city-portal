namespace CityPortal.News;

/// <summary>A permitted retained event and version snapshot.</summary>
public sealed record ArticleHistoryResponse(Guid Id, Guid ActorId, string Action, string Reason, Guid? ReviewerId, long Version, DateTimeOffset CreatedAtUtc, ArticleResponse Revision);
