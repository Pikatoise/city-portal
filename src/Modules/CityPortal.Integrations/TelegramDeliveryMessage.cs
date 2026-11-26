namespace CityPortal.Integrations;

public sealed record TelegramDeliveryMessage(Guid DeliveryId, int Attempt);
