namespace CityPortal.Media;

internal sealed class ImageTemplate
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ImageId { get; set; }
    public string Name { get; set; } = "";
    public string Approval { get; set; } = "Pending";
    public bool Submitted { get; set; }
    public bool Archived { get; set; }
    public bool Blocked { get; set; }
    public string? Reason { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
