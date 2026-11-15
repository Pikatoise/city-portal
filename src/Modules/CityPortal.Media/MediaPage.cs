namespace CityPortal.Media;

public sealed record MediaPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
