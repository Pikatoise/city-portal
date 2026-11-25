namespace CityPortal.Classifieds;

/// <summary>Published listing and current moderation version.</summary>
public sealed record ListingModerationItem(Guid Id, string Title, int Version, bool Blocked);
