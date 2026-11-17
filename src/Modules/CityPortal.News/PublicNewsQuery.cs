namespace CityPortal.News;

/// <summary>Untrusted public discovery input; validated by <see cref="PublicNewsDiscoveryOperations" />.</summary>
public sealed record PublicNewsQuery(string? Query, Guid? CategoryId, Guid? TagId, DateOnly? FromDate, DateOnly? ToDate, int Page, int PageSize, bool Featured = false, Guid? RelatedTo = null);
