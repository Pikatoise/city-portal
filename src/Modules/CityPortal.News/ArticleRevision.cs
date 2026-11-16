using System.Text.Json;
using NpgsqlTypes;

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

    // Generated from the revision content so discovery filters and search always track every write.
    public NpgsqlTsVector SearchVector { get; set; } = NpgsqlTsVector.Empty;
    public string Summary { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public JsonDocument Tags { get; set; } = JsonDocument.Parse("[]");
    public Guid? CoverMediaId { get; set; }
    public string? CoverAlt { get; set; }
}
