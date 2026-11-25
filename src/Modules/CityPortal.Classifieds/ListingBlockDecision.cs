namespace CityPortal.Classifieds;

internal sealed class ListingBlockDecision
{
    public Guid ListingId { get; init; }

    public int Version { get; init; }

    public Guid ActorId { get; init; }

    public DateTimeOffset DecidedAtUtc { get; init; }

    public required string Reason { get; init; }
}
