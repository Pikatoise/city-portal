namespace CityPortal.News;

internal sealed class ArticleHistory
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? ReviewerId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
