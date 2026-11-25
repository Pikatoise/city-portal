namespace CityPortal.Classifieds;

/// <summary>Submitted advertisement and its category and district names.</summary>
public sealed record ListingReviewPreview(ListingDraftResponse Submission, string Category, string District)
{
    public ListingReviewOriginal? Original { get; init; }
}
