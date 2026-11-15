namespace CityPortal.Notifications;

/// <summary>Independent channel switches for one workflow event type.</summary>
public sealed record NotificationPreferenceResponse(string Type, bool Account, bool Email);
