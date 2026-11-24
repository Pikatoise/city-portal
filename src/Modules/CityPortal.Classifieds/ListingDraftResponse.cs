using CityPortal.Media;

namespace CityPortal.Classifieds;

/// <summary>A private draft and its current version.</summary>
public sealed record ListingDraftResponse(Guid Id, ListingDraftInput Details, DateTimeOffset UpdatedAtUtc, int Version, string State)
{
    public ListingPhotoResponse[] Photos { get; init; } = [];
}
