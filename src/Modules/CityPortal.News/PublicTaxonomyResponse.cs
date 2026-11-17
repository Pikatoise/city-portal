namespace CityPortal.News;

/// <summary>A taxonomy name without private counts or staff metadata.</summary>
public sealed record PublicTaxonomyResponse(Guid Id, string Name);
