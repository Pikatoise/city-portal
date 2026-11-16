namespace CityPortal.News;

/// <summary>A submitted snapshot and current assignment without unsent author content.</summary>
public sealed record ReviewResponse(ArticleResponse Article, Guid AuthorId, Guid? ReviewerId, string PublicationState, bool Deleted, string WorkingState);
