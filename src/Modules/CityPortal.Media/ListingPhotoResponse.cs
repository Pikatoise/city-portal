namespace CityPortal.Media;

/// <summary>A listing photo and its processing status.</summary>
public sealed record ListingPhotoResponse(ImageResponse Image, string AlternativeText);
