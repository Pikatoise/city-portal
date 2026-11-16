using CityPortal.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Produces("application/json")]
[Route("api/news")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NewsController : ControllerBase
{
    private readonly PublicArticleOperations _articles;
    private readonly PublicNewsDiscoveryOperations _discovery;

    public NewsController(PublicArticleOperations articles, PublicNewsDiscoveryOperations discovery)
    {
        _articles = articles;
        _discovery = discovery;
    }

    [HttpGet("articles", Name = "listPublicArticles")]
    [ProducesResponseType<NewsPage<PublicArticleSummaryResponse>>(200)]
    [ProducesResponseType(400)]
    public Task<NewsPage<PublicArticleSummaryResponse>> ListAsync(CancellationToken cancellationToken, string? query = null, Guid? categoryId = null,
        Guid? tagId = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 20, bool featured = false) =>
        _discovery.ListAsync(new PublicNewsQuery(query, categoryId, tagId, from, to, page, pageSize, featured), cancellationToken);

    [HttpGet("homepage", Name = "getPublicHomepage")]
    public Task<PublicHomepageResponse> HomepageAsync(CancellationToken cancellationToken) => _discovery.HomepageAsync(cancellationToken);

    [HttpGet("articles/{id:guid}/related", Name = "getRelatedArticles")]
    public async Task<PublicArticleSummaryResponse[]> RelatedAsync(Guid id, CancellationToken cancellationToken) =>
        (await _discovery.ListAsync(new PublicNewsQuery(null, null, null, null, null, 1, 5, RelatedTo: id), cancellationToken)).Items;

    [HttpGet("{kind:regex(^(categories|tags)$)}", Name = "listPublicTaxonomy")]
    public Task<NewsPage<PublicTaxonomyResponse>> TaxonomyAsync(string kind, CancellationToken cancellationToken, int page = 1, int pageSize = 100) =>
        _discovery.TaxonomyAsync(kind, page, pageSize, cancellationToken);

    [HttpGet("articles/{id:guid}", Name = "getPublicArticle")]
    [ProducesResponseType<PublicArticleResponse>(200)]
    [ProducesResponseType(404)]
    public Task<PublicArticleResponse> GetAsync(Guid id, CancellationToken cancellationToken, string? slug = null) =>
        _articles.GetAsync(id, slug, cancellationToken);
}
