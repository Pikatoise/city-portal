using System.Text.Json;

namespace CityPortal.News;

/// <summary>Revision-owned article fields; incomplete drafts may omit submission fields.</summary>
public sealed record ArticleContent
{
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public int SchemaVersion { get; init; } = 1;
    public JsonElement Body { get; init; } = JsonSerializer.SerializeToElement(new { type = "doc", content = new[] { new { type = "paragraph" } } });
    public Guid? CategoryId { get; init; }
    public Guid[] TagIds { get; init; } = [];
    public ArticleImage? Cover { get; init; }
    public ArticleImage[] Gallery { get; init; } = [];
    public bool UsesOthers { get; init; }
    public ArticleSource[] Sources { get; init; } = [];
    public string SeoTitle { get; init; } = "";
    public string SeoDescription { get; init; } = "";
}
