using System.Security.Claims;
using CityPortal.CitizenSubmissions;
using CityPortal.Media;
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

    [HttpGet("{id:guid}/images", Name = "listStaffSuggestionImages")]
    public Task<ImageResponse[]> ImagesAsync(Guid id, CancellationToken cancellationToken) =>
        _operations.ImagesAsync(ActorId, SessionId, id, cancellationToken);

    [HttpGet("{id:guid}/images/{imageId:guid}", Name = "openStaffSuggestionImage")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> OpenImageAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        Stream image = await _operations.OpenImageAsync(ActorId, SessionId, id, imageId, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image, "image/webp");
    }

    [HttpPost("{id:guid}/import-images", Name = "importSuggestionImages")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ImportImagesAsync(Guid id, ImportSuggestionImagesRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string csrf, CancellationToken cancellationToken)
    {
        await _operations.ImportImagesAsync(ActorId, SessionId, id, request.ImageIds, cancellationToken);
        return NoContent();
    }

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
