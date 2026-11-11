namespace CityPortal.Notifications;

public static class DeliverAccountMailHandler
{
    public static Task HandleAsync(DeliverAccountMail message, AccountMailDeliveryService delivery, CancellationToken cancellationToken)
        => delivery.DeliverAsync(message, cancellationToken);
}
