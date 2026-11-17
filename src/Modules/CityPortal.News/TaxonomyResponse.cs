namespace CityPortal.News;

/// <summary>A category or tag, including its archival and optimistic-concurrency state.</summary>
public sealed record TaxonomyResponse(Guid Id, string Kind, string Name, bool Archived, long Version);
