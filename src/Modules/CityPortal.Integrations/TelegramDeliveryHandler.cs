namespace CityPortal.Integrations;

public static class TelegramDeliveryHandler
{
    public static Task HandleAsync(TelegramDeliveryMessage message, TelegramDeliveryWorker worker, CancellationToken cancellationToken) =>
        worker.DeliverAsync(message, cancellationToken);
}
