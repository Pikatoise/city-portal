namespace CityPortal.Notifications;

internal sealed class EmailAttempt
{
    public Guid Id { get; set; }

    public Guid DeliveryId { get; set; }

    public int Number { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset FinishedAtUtc { get; set; }

    public string Result { get; set; } = "";

    public string? ErrorCode { get; set; }
}
