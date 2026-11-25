namespace CityPortal.Classifieds;

/// <summary>A recorded listing review or block. Actor details are available to authorized staff.</summary>
public sealed record ListingHistoryItem(Guid ListingId, int Version, string Outcome, DateTimeOffset DecidedAtUtc,
    string? Reason, Guid? ActorId, string? ActorName, string? CurrentTitle);
