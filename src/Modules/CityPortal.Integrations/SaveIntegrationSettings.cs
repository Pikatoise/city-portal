using System.Text.Json.Serialization;

namespace CityPortal.Integrations;

public sealed record SaveIntegrationSettings(
    [property: JsonRequired] int Version,
    [property: JsonRequired] bool RssEnabled,
    [property: JsonRequired] bool WeatherEnabled,
    [property: JsonRequired] double? Latitude,
    [property: JsonRequired] double? Longitude,
    [property: JsonRequired] bool TelegramEnabled,
    [property: JsonRequired] string? TelegramChannel,
    [property: JsonRequired] bool AiEnabled,
    [property: JsonRequired] bool McpEnabled);
