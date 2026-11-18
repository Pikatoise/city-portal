namespace CityPortal.News;

internal sealed class NewsArticle
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public Guid? IdeaContributorId { get; set; }
    public long Version { get; set; } = 1;
    public string PublicationState { get; set; } = "NeverPublished";
    public bool Featured { get; set; }
    public Guid WorkingRevisionId { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public Guid? LastSubmittedRevisionId { get; set; }
    public Guid? ReviewerId { get; set; }
    public DateTimeOffset? FirstPublishedAtUtc { get; set; }
    public DateOnly? FirstPublishedLocalDate { get; set; }
    public string? PublicAddress { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
