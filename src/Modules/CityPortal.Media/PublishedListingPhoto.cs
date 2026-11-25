namespace CityPortal.Media;

/// <summary>Photo information safe to include in a published listing.</summary>
public sealed record PublishedListingPhoto(Guid Id, string AlternativeText);
