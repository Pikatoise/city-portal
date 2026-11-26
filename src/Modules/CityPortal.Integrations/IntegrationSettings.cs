namespace CityPortal.Integrations;

public sealed class IntegrationSettings
{
    public int Id { get; set; } = 1;

    public int Version { get; set; }

    public bool RssEnabled { get; set; }

    public bool WeatherEnabled { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public bool TelegramEnabled { get; set; }

    public string? TelegramChannel { get; set; }

    public bool AiEnabled { get; set; }

    public bool McpEnabled { get; set; }
}
