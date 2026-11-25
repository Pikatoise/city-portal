namespace CityPortal.Classifieds;

internal sealed class ListingPublication
{
    public Guid ListingId { get; init; }

    public Guid RevisionId { get; set; }

    public Guid OwnerId { get; init; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public Guid CategoryId { get; set; }

    public Guid DistrictId { get; set; }

    public long? Price { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset PublishedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public Guid? OriginalRevisionId { get; set; }

    public string? OriginalDetails { get; set; }

    public bool WithdrawnByOwner { get; set; }

    public bool HiddenByAccountBlock { get; set; }

    public bool HiddenByListingBlock { get; set; }

    internal ListingDraftInput Details() => new()
    {
        Title = Title,
        Description = Description,
        CategoryId = CategoryId,
        DistrictId = DistrictId,
        Price = Price,
        Phone = Phone,
        Email = Email
    };
}
