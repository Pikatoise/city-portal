namespace CityPortal.Integrations;

/// <summary>Delivery status and optional link to a post in the configured public channel.</summary>
public sealed record TelegramDeliveryResponse(long Version, Guid RevisionId, string State, string Channel, string Text, string? PostUrl, string? ErrorCode);
