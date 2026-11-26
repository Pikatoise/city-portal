namespace CityPortal.Integrations;

internal sealed class TelegramDeliveryEvent
{
    public Guid Id { get; set; }

    public Guid DeliveryId { get; set; }

    public Guid? ActorId { get; set; }

    public Guid RevisionId { get; set; }

    public string Action { get; set; } = "";

    public int Attempt { get; set; }

    public DateTimeOffset AtUtc { get; set; }
}
