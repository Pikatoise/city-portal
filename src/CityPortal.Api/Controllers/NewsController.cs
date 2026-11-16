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

    public NewsController(PublicArticleOperations articles)
    {
        _articles = articles;
    }

    [HttpGet("articles/{id:guid}", Name = "getPublicArticle")]
    [ProducesResponseType<PublicArticleResponse>(200)]
    [ProducesResponseType(404)]
    public Task<PublicArticleResponse> GetAsync(Guid id, CancellationToken cancellationToken, string? slug = null) =>
        _articles.GetAsync(id, slug, cancellationToken);
}
