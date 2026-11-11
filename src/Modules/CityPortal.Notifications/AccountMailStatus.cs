namespace CityPortal.Notifications;

public sealed class AccountMailStatus
{
    public string State { get; init; } = "Pending";

    public DateTimeOffset? LastAttemptAtUtc { get; init; }
}
