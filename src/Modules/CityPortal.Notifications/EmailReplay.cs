namespace CityPortal.Notifications;

internal sealed class EmailReplay
{
    public Guid Id { get; set; }

    public Guid ActorId { get; set; }

    public Guid DeliveryId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public long PreviousVersion { get; set; }
}
