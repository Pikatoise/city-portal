namespace CityPortal.Media;

internal sealed class MediaImage
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string State { get; set; } = "Processing";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
    public bool EverAttached { get; set; }
    public bool ForSuggestion { get; set; }
    public bool ForListing { get; set; }
    public string? ErrorCode { get; set; }
}
