namespace CityPortal.News;

/// <summary>A private author-owned working article and its expected version.</summary>
public sealed record ArticleResponse(Guid Id, Guid RevisionId, long Version, string State, ArticleContent Content, string PreviewHtml, DateTimeOffset UpdatedAtUtc, bool PreviouslySubmitted = false, string PublicationState = "NeverPublished");
