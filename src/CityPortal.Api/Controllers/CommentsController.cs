using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Comments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Route("api/news/articles/{articleId:guid}/comments")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CommentsController : ControllerBase
{
    private readonly CommentOperations _comments;

    public CommentsController(CommentOperations comments)
    {
        _comments = comments;
    }

    [AllowAnonymous]
    [HttpGet(Name = "listArticleComments")]
    [ProducesResponseType<CommentPage>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<CommentPage> ListAsync(Guid articleId, CancellationToken cancellationToken, string? cursor = null)
    {
        bool blocked = User.HasClaim("account_blocked", bool.TrueString);
        bool canModerate = !blocked && User.HasClaim("email_confirmed", bool.TrueString)
            && (User.IsInRole("Moderator") || User.IsInRole("Administrator"));
        return _comments.ListAsync(articleId, cursor, blocked ? null : ActorId, canModerate, cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost(Name = "createArticleComment")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<CommentResponse>(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> CreateAsync(Guid articleId, CreateCommentRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        CommentResponse comment = await _comments.CreateAsync(articleId, ActorId, SessionId, request, cancellationToken);
        return StatusCode(201, comment);
    }

    [Authorize]
    [HttpGet("/api/account/comments", Name = "listOwnComments")]
    [ProducesResponseType<OwnCommentPage>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<OwnCommentPage>> ListOwnAsync(CancellationToken cancellationToken, int page = 1)
    {
        if (ActorId is not Guid actorId)
            return Unauthorized();
        bool canDelete = !User.HasClaim("account_blocked", bool.TrueString);
        return await _comments.ListOwnAsync(actorId, page, canDelete, cancellationToken);
    }

    [Authorize]
    [HttpDelete("/api/account/comments/{id:guid}", Name = "deleteOwnComment")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteAsync(Guid id,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        if (ActorId is not Guid actorId || SessionId is not Guid sessionId)
            return Unauthorized();
        await _comments.DeleteAsync(id, actorId, sessionId, cancellationToken);
        return NoContent();
    }

    private Guid? ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    private Guid? SessionId => Guid.TryParse(User.FindFirstValue("session"), out Guid id) ? id : null;
}
