namespace CityPortal.News;

/// <summary>Bounded, current-publication-only homepage blocks.</summary>
public sealed record PublicHomepageResponse(PublicArticleSummaryResponse[] Featured, PublicArticleSummaryResponse[] Latest, PublicCategoryBlock[] Categories);
