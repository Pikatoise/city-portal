namespace CityPortal.Notifications;

internal sealed class WorkflowReceipt
{
    public Guid EventId { get; set; }
    public Guid AccountId { get; set; }
    public string Type { get; set; } = "";
    public Guid NotificationId { get; set; }
}
