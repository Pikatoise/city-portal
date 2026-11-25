namespace CityPortal.Identity;

/// <summary>An account block or unblock. Actor details are available to authorized staff.</summary>
public sealed record AccountBlockHistoryItem(Guid Id, bool Blocked, DateTimeOffset DecidedAtUtc, string? Reason, Guid? ActorId, string? ActorName);
