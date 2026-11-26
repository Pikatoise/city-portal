namespace CityPortal.Comments;

internal sealed class ArticleComment
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ArticleId { get; init; }

    public Guid? OwnerId { get; init; }

    public required string Body { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? DeletedAtUtc { get; set; }

    public DateTimeOffset? HiddenAtUtc { get; set; }
}
