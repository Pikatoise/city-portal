namespace CityPortal.Notifications;

/// <summary>A content-free workflow notice; links grant no resource permissions.</summary>
public sealed record AccountNotificationResponse(Guid Id, string Type, string Message, Guid ResourceId, DateTimeOffset CreatedAtUtc, bool Read);
