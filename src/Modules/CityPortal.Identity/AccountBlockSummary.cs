namespace CityPortal.Identity;

/// <summary>Account state visible in the moderation workspace.</summary>
public sealed record AccountBlockSummary(Guid Id, string Email, string DisplayName, long Version, string? BlockReason);
