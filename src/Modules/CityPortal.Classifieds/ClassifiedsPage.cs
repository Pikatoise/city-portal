namespace CityPortal.Classifieds;

/// <summary>A page of up to 20 records and an indication that more records exist.</summary>
public sealed record ClassifiedsPage<T>(T[] Items, bool HasNextPage);
