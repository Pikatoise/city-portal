namespace CityPortal.Classifieds;

internal sealed class ListingPublication
{
    public Guid ListingId { get; init; }

    public Guid RevisionId { get; init; }

    public Guid OwnerId { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public Guid CategoryId { get; init; }

    public Guid DistrictId { get; init; }

    public long? Price { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public DateTimeOffset PublishedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool HiddenByAccountBlock { get; set; }

    public bool HiddenByListingBlock { get; set; }
}
