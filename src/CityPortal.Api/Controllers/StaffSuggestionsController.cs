using System.Security.Claims;
using CityPortal.CitizenSubmissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/staff/suggestions")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffSuggestionsController : ControllerBase
{
    private readonly StaffSuggestionOperations _operations;

    public StaffSuggestionsController(StaffSuggestionOperations operations)
    {
        _operations = operations;
    }

    [HttpGet("queue", Name = "listAuthorSuggestions")]
    public Task<StaffSuggestionPage> QueueAsync(CancellationToken cancellationToken, bool mine = false, int page = 1) =>
        _operations.ListAsync(ActorId, SessionId, moderation: false, mine, page, cancellationToken);

    [HttpGet("moderation", Name = "listModerationSuggestions")]
    public Task<StaffSuggestionPage> ModerationAsync(CancellationToken cancellationToken, int page = 1) =>
        _operations.ListAsync(ActorId, SessionId, moderation: true, mine: false, page, cancellationToken);

    [HttpPost("{id:guid}/claim", Name = "claimSuggestion")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ClaimAsync(Guid id, SuggestionVersionRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string csrf, CancellationToken cancellationToken)
    {
        await _operations.ChangeAssignmentAsync(ActorId, SessionId, id, request.Version, claim: true, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/return", Name = "returnSuggestion")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ReturnAsync(Guid id, SuggestionVersionRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string csrf, CancellationToken cancellationToken)
    {
        await _operations.ChangeAssignmentAsync(ActorId, SessionId, id, request.Version, claim: false, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/delete", Name = "deleteSuggestion")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> DeleteAsync(Guid id, DeleteSuggestionRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string csrf, CancellationToken cancellationToken)
    {
        await _operations.DeleteAsync(ActorId, SessionId, id, request, cancellationToken);
        return NoContent();
    }

    // The validated cookie handler always rebuilds both identifiers before authorizing a request.
    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);
}
