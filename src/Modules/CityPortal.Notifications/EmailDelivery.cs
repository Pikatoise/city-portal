namespace CityPortal.Notifications;

internal sealed class EmailDelivery
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid LinkId { get; set; }

    public string Purpose { get; set; } = "";

    public string ProtectedPayload { get; set; } = "";

    public string State { get; set; } = "Queued";

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public long Version { get; set; } = 1;
}
