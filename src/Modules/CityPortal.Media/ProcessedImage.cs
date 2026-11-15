namespace CityPortal.Media;

public sealed record ProcessedImage(string Variant, int Width, int Height, byte[] Bytes);
