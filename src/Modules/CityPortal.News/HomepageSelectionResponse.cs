namespace CityPortal.News;

/// <summary>Versioned editorial homepage category selection.</summary>
public sealed record HomepageSelectionResponse(long Version, Guid? FirstCategoryId, Guid? SecondCategoryId);
