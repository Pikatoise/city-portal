using CityPortal.Media;

namespace CityPortal.Classifieds;

/// <summary>A private draft and its current version.</summary>
public sealed record ListingDraftResponse(Guid Id, ListingDraftInput Details, DateTimeOffset UpdatedAtUtc, int Version, string State)
{
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public bool CanSubmit { get; init; }

    public bool CanRenew { get; init; }

    public bool CanWithdraw { get; init; }

    public string? ReviewReason { get; init; }

    public ListingPhotoResponse[] Photos { get; init; } = [];
}
