namespace CityPortal.Notifications;

// Only opaque identifiers enter transport storage and routine error queues.
public sealed class DeliverAccountMail
{
    public Guid DeliveryId { get; init; }

    public int Attempt { get; init; }
}
