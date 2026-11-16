namespace CityPortal.News;

internal sealed class CurationChange
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid? ArticleId { get; set; }
    public string Action { get; set; } = "";
    public string SelectionJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
