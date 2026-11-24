namespace CityPortal.Classifieds;

internal sealed class CatalogEntry
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = "";

    public string Name { get; set; } = "";

    public string NormalizedName { get; set; } = "";

    public bool RequiresPrice { get; set; }

    public bool Archived { get; set; }

    public int Version { get; set; } = 1;
}
