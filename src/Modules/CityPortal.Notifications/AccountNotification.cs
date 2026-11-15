namespace CityPortal.Notifications;

internal sealed class AccountNotification
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Type { get; set; } = "";
    public Guid ResourceId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool Read { get; set; }
}
