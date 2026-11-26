using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.News;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CityPortal.Api;

[McpServerToolType]
internal sealed class NewsMcpTools
{
    private readonly PublicNewsDiscoveryOperations _discovery;
    private readonly PublicArticleOperations _articles;
    private readonly string _publicOrigin;

    public NewsMcpTools(PublicNewsDiscoveryOperations discovery, PublicArticleOperations articles, IConfiguration configuration)
    {
        _discovery = discovery;
        _articles = articles;
        _publicOrigin = NewsMcp.PublicOrigin(configuration) ?? throw new InvalidOperationException("MCP requires an HTTPS public origin.");
    }

    [McpServerTool(Name = "list_latest_news", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List up to 20 published CityPortal news articles, newest first by original publication date.")]
    public Task<NewsList> ListLatestAsync(CancellationToken cancellationToken) => ListAsync(null, cancellationToken);

    [McpServerTool(Name = "search_news", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Search the current public CityPortal news by text and return up to 20 matches in relevance order.")]
    public Task<NewsList> SearchAsync([Description("Search text, 1 to 200 characters.")] string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200)
            throw new McpException("Provide search text of 1 to 200 characters.");
        return ListAsync(query, cancellationToken);
    }

    [McpServerTool(Name = "get_article", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read the current public text of a CityPortal article using its address from the news list or search.")]
    public async Task<Article> GetAsync([Description("Article address returned by a news tool.")] string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 160 || address.Contains('/'))
            throw new McpException("Provide the article address returned by a news tool.");

        PublicArticleResponse article;
        try
        {
            article = await _articles.GetAsync(address, cancellationToken);
        }
        catch (AccountOperationException exception) when (exception.Status == 404)
        {
            throw new McpException("The article is unavailable.");
        }
        var body = new StringBuilder();
        AppendText(article.Content.Body, body);
        return new Article(article.Content.Title, article.Content.Summary, body.ToString().Trim(),
            _publicOrigin + article.CanonicalUrl, article.FirstPublishedAtUtc);
    }

    private async Task<NewsList> ListAsync(string? query, CancellationToken cancellationToken)
    {
        NewsPage<PublicArticleSummaryResponse> page = await _discovery.ListAsync(new PublicNewsQuery(query, null, null, null, null, 1, 20), cancellationToken);
        return new NewsList(page.Items.Select(item => new NewsItem(item.CanonicalUrl["/news/".Length..], item.Title,
            item.Summary, _publicOrigin + item.CanonicalUrl, item.FirstPublishedAtUtc)).ToArray());
    }

    private static void AppendText(JsonElement node, StringBuilder builder)
    {
        string? type = node.GetProperty("type").GetString();
        if (type == "text")
            builder.Append(node.GetProperty("text").GetString());
        if (node.TryGetProperty("content", out JsonElement children))
            foreach (JsonElement child in children.EnumerateArray())
                AppendText(child, builder);
        if (type is "paragraph" or "heading" or "hardBreak")
            builder.Append('\n');
    }

    public sealed record NewsList(NewsItem[] Items);

    public sealed record NewsItem(string Address, string Title, string Summary, string Url, DateTimeOffset PublishedAtUtc);

    public sealed record Article(string Title, string Summary, string Text, string Url, DateTimeOffset PublishedAtUtc);
}
