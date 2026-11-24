namespace CityPortal.Classifieds;

/// <summary>A pending advertisement in the staff review queue.</summary>
public sealed record ListingReviewQueueItem(Guid Id, string Title, DateTimeOffset SubmittedAtUtc);
