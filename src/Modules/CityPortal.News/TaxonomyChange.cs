namespace CityPortal.News;

internal sealed class TaxonomyChange
{
    public Guid Id { get; set; }
    public Guid TaxonomyId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Archived { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
