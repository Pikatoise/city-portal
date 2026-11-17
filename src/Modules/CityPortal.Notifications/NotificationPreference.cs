namespace CityPortal.Notifications;

internal sealed class NotificationPreference
{
    public Guid AccountId { get; set; }
    public string Type { get; set; } = "";
    public bool Account { get; set; } = true;
    public bool Email { get; set; } = true;
}
