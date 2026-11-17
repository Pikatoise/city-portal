namespace CityPortal.News;

/// <summary>Canonical document data and independently rendered safe preview.</summary>
public sealed record ValidatedArticleContent(ArticleContent Content, string PreviewHtml, string PlainText, Guid[] ImageIds);
