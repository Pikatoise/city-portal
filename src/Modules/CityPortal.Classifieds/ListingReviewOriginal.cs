using CityPortal.Media;

namespace CityPortal.Classifieds;

public sealed record ListingReviewOriginal(ListingDraftInput Details, string Category, string District, PublishedListingPhoto[] Photos);
