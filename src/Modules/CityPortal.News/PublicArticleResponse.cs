namespace CityPortal.News;

/// <summary>Only the current publication, with no private revision identifiers or workflow state.</summary>
public sealed record PublicArticleResponse(Guid Id, string CanonicalUrl, ArticleContent Content, string PreviewHtml,
    string AuthorDisplayName, DateTimeOffset FirstPublishedAtUtc, DateTimeOffset UpdatedAtUtc);
