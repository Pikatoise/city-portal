namespace CityPortal.News;

/// <summary>Publication state exposed to the module that supplied a story idea.</summary>
public sealed record SuggestionArticleStatus(Guid ArticleId, bool HasBeenPublished, string? PublicUrl);
