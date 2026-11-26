namespace CityPortal.Integrations;

/// <summary>The public revision and current delivery state shown to the editor.</summary>
public sealed record TelegramAnnouncementResponse(Guid? RevisionId, string? SuggestedText, string? ArticleUrl,
    bool Enabled, bool AiEnabled, string? Channel, TelegramDeliveryResponse? Delivery);
