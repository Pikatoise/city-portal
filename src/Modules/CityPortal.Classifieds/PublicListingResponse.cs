using CityPortal.Media;

namespace CityPortal.Classifieds;

/// <summary>Approved listing fields exposed to anonymous visitors.</summary>
public sealed record PublicListingResponse(Guid Id, string Title, string Description, string Category, string District,
    long? Price, string? Phone, DateTimeOffset PublishedAtUtc, DateTimeOffset ExpiresAtUtc, PublishedListingPhoto[] Photos);
