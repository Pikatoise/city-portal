namespace CityPortal.News;

/// <summary>A public card summary; never exposes working revision content, body, or storage keys.</summary>
public sealed record PublicArticleSummaryResponse(Guid Id, string Title, string Summary, string CanonicalUrl, Guid? CategoryId, Guid[] TagIds,
    ArticleImage? Cover, DateTimeOffset FirstPublishedAtUtc, string AuthorDisplayName);
