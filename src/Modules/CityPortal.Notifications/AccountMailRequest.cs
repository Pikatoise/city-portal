namespace CityPortal.Notifications;

public sealed class AccountMailRequest
{
    public Guid MessageId { get; init; }

    public Guid AccountId { get; init; }

    public Guid LinkId { get; init; }

    public string Purpose { get; init; } = "confirmation";

    public string Recipient { get; init; } = "";

    public string Token { get; init; } = "";

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }
}
