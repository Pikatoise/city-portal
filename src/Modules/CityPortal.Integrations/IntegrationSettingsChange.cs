namespace CityPortal.Integrations;

internal sealed class IntegrationSettingsChange
{
    public int Version { get; set; }

    public Guid ActorId { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }

    public string Settings { get; set; } = "";
}
