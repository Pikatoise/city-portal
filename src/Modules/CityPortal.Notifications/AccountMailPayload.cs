namespace CityPortal.Notifications;

internal sealed class AccountMailPayload
{
    public string Recipient { get; init; } = "";

    public string Token { get; init; } = "";
}
