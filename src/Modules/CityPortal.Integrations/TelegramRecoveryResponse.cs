namespace CityPortal.Integrations;

/// <summary>Identifies a delivery requiring the editor to inspect the Telegram channel.</summary>
public sealed record TelegramRecoveryResponse(Guid ArticleId, string Channel, string Text, DateTimeOffset UpdatedAtUtc);
