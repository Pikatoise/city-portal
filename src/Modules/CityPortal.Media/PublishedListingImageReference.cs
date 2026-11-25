namespace CityPortal.Media;

internal sealed class PublishedListingImageReference
{
    public Guid RevisionId { get; init; }

    public Guid ImageId { get; init; }

    public required string AlternativeText { get; init; }

    public int Position { get; init; }
}
