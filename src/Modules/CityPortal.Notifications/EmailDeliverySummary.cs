using System.Text.Json.Serialization;

namespace CityPortal.Notifications;

/// <summary>Delivery metadata and sanitized failures; excludes recipient and protected mail content.</summary>
public sealed record EmailDeliverySummary(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] Guid AccountId,
    [property: JsonRequired] string Purpose,
    [property: JsonRequired] string State,
    [property: JsonRequired] int AttemptCount,
    [property: JsonRequired] int RetryCount,
    [property: JsonRequired] DateTimeOffset CreatedAtUtc,
    [property: JsonRequired] DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? ErrorCode,
    [property: JsonRequired] long Version);
