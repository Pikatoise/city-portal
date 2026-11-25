namespace CityPortal.Classifieds;

internal sealed class ListingDraft
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }

    public Guid? DistrictId { get; set; }

    public long? Price { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public DateTimeOffset? EditReviewQueuedAtUtc { get; set; }

    public string? ReviewOutcome { get; set; }

    public int Version { get; set; } = 1;
}
