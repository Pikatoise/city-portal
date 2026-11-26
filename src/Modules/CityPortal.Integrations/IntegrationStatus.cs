namespace CityPortal.Integrations;

public sealed record IntegrationStatus(SaveIntegrationSettings Settings, bool TelegramConfigured, bool AiConfigured, bool McpConfigured,
    DateTimeOffset? WeatherRefreshedAtUtc, int TelegramPending, int TelegramNeedsAttention);
