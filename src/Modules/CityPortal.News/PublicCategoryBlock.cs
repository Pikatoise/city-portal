namespace CityPortal.News;

/// <summary>A selected category and up to five current publications.</summary>
public sealed record PublicCategoryBlock(PublicTaxonomyResponse Category, PublicArticleSummaryResponse[] Articles);
