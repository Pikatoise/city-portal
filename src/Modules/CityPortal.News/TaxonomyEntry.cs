namespace CityPortal.News;

internal sealed class TaxonomyEntry
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public bool Archived { get; set; }
    public long Version { get; set; } = 1;
}
