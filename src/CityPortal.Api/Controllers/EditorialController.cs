using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Api.Contracts;
using CityPortal.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/editorial")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EditorialController : ControllerBase
{
    private readonly ArticleOperations _articles;
    private readonly TaxonomyOperations _taxonomy;
    private readonly ReviewOperations _reviews;

    public EditorialController(ArticleOperations articles, TaxonomyOperations taxonomy, ReviewOperations reviews)
    {
        _articles = articles;
        _taxonomy = taxonomy;
        _reviews = reviews;
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);

    [HttpGet("review-queue", Name = "listArticleReviewQueue")]
    public Task<NewsPage<ReviewSummaryResponse>> ReviewQueueAsync(CancellationToken cancellationToken, bool mine = false, int page = 1, int pageSize = 20) =>
        _reviews.QueueAsync(ActorId, SessionId, mine, page, pageSize, cancellationToken);

    [HttpGet("reviews/{id:guid}", Name = "getArticleReview")]
    public Task<ReviewResponse> ReviewAsync(Guid id, CancellationToken cancellationToken) => _reviews.GetAsync(ActorId, SessionId, id, cancellationToken);

    [HttpGet("articles/{id:guid}/history", Name = "getArticleHistory")]
    public Task<NewsPage<ArticleHistoryResponse>> HistoryAsync(Guid id, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
        _reviews.HistoryAsync(ActorId, SessionId, id, page, pageSize, cancellationToken);

    [HttpPost("articles/{id:guid}/{operation:regex(^(submit|recall|claim|release|takeover|return|reject|approve|publish|propose|withdraw)$)}", Name = "actOnArticleReview")]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType<ArticleResponse>(200)]
    [ProducesResponseType(409)]
    public Task<ArticleResponse> ReviewActionAsync(Guid id, string operation, ReviewArticleRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        _reviews.ActAsync(ActorId, SessionId, id, request.Version, operation, request.Reason, cancellationToken);

    [HttpGet("articles", Name = "listOwnArticles")]
    public Task<NewsPage<ArticleSummaryResponse>> ListAsync(CancellationToken cancellationToken, int page = 1, int pageSize = 20, string? search = null) =>
        _articles.ListAsync(ActorId, SessionId, page, pageSize, search, cancellationToken);

    [HttpGet("articles/{id:guid}", Name = "getOwnArticle")]
    public Task<ArticleResponse> GetAsync(Guid id, CancellationToken cancellationToken) => _articles.GetAsync(ActorId, SessionId, id, cancellationToken);

    [HttpPost("articles", Name = "createArticleDraft")]
    [ProducesResponseType<ArticleResponse>(201)]
    public async Task<ActionResult<ArticleResponse>> CreateAsync([FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        ArticleResponse article = await _articles.CreateAsync(ActorId, SessionId, cancellationToken);
        return Created($"/api/editorial/articles/{article.Id}", article);
    }

    [HttpPatch("articles/{id:guid}", Name = "saveArticleDraft")]
    [Consumes("application/json")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    [ProducesResponseType<ArticleResponse>(200)]
    [ProducesResponseType(409)]
    public Task<ArticleResponse> SaveAsync(Guid id, SaveArticleRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        _articles.SaveAsync(ActorId, SessionId, id, request.Version, request.Content, cancellationToken);

    [HttpDelete("articles/{id:guid}", Name = "deleteArticleDraft")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteAsync(Guid id, [FromQuery, Range(1, long.MaxValue)] long version, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        await _articles.DeleteAsync(ActorId, SessionId, id, version, cancellationToken);
        return NoContent();
    }

    [HttpGet("{kind}", Name = "listEditorialTaxonomy")]
    public Task<NewsPage<TaxonomyResponse>> TaxonomyAsync(string kind, CancellationToken cancellationToken, int page = 1, int pageSize = 100) =>
        _taxonomy.ListAsync(ActorId, SessionId, kind, page, pageSize, cancellationToken);

    [HttpPost("{kind}", Name = "createEditorialTaxonomy")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<TaxonomyResponse>(201)]
    public async Task<ActionResult<TaxonomyResponse>> CreateTaxonomyAsync(string kind, SaveTaxonomyRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        TaxonomyResponse entry = await _taxonomy.SaveAsync(ActorId, SessionId, kind, null, 0, request.Name, request.Archived, cancellationToken);
        return StatusCode(201, entry);
    }

    [HttpPatch("{kind}/{id:guid}", Name = "saveEditorialTaxonomy")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    public Task<TaxonomyResponse> SaveTaxonomyAsync(string kind, Guid id, SaveTaxonomyRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        _taxonomy.SaveAsync(ActorId, SessionId, kind, id, request.Version, request.Name, request.Archived, cancellationToken);
}
