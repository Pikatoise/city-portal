namespace CityPortal.News;

/// <summary>A bounded, deterministically ordered page.</summary>
public sealed record NewsPage<T>(T[] Items, int Total, int Page, int PageSize);
