namespace CityPortal.Classifieds;

/// <summary>A selectable category or district, including its current version.</summary>
public sealed record CatalogEntryResponse(Guid Id, string Name, bool RequiresPrice, bool Archived, int Version);
