namespace CityPortal.News;

internal sealed class ArticleRevision
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string State { get; set; } = "Draft";
    public string ContentJson { get; set; } = "{}";
    public string Title { get; set; } = "";
    public string PlainText { get; set; } = "";
    public string PreviewHtml { get; set; } = "";
    public bool Retained { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
