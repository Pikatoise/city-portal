namespace CityPortal.Media;

internal sealed class ImageOutput
{
    public Guid ImageId { get; set; }
    public string Variant { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[]? PendingBytes { get; set; }
}
