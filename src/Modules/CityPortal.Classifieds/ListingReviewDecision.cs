namespace CityPortal.Classifieds;

internal sealed class ListingReviewDecision
{
    public required Guid ListingId { get; init; }

    public required int SubmittedVersion { get; init; }

    public required Guid ActorId { get; init; }

    public required DateTimeOffset DecidedAtUtc { get; init; }

    public required string Outcome { get; init; }

    public string? Reason { get; init; }
}
