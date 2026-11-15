namespace CityPortal.Media;

internal sealed class TemplateEvent
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public Guid ImageId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public long Version { get; set; }
    public string Approval { get; set; } = "";
    public bool Archived { get; set; }
    public bool Blocked { get; set; }
    public string Name { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    // Durable integration handoff; owning history survives downstream notification delivery.
    public DateTimeOffset? NotificationDispatchedAtUtc { get; set; }
}
