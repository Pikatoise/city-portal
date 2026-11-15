namespace CityPortal.Media;

internal sealed class TemplateRemovalRequest
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public Guid OwnerId { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public Guid? ResolvedBy { get; set; }
}
