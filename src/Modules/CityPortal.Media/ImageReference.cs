namespace CityPortal.Media;

internal sealed class ImageReference
{
    public bool Current { get; set; } = true;
    public bool AuthorVisible { get; set; } = true;
    public Guid RevisionId { get; set; }
    public Guid ImageId { get; set; }
    public Guid AuthorId { get; set; }
    public Guid? ReviewerId { get; set; }
    public bool Submitted { get; set; }
    public bool Public { get; set; }
}
