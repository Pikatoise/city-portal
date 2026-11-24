namespace CityPortal.Media;

internal sealed class ListingImageReference
{
    public Guid ImageId { get; set; }

    public Guid DraftId { get; set; }

    public string AlternativeText { get; set; } = "";

    public int Position { get; set; }
}
