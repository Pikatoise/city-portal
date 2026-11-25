using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Classifieds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/staff/classifieds/published")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassifiedsModerationController : ControllerBase
{
    private readonly ListingPublications _publications;

    public ClassifiedsModerationController(ListingPublications publications)
    {
        _publications = publications;
    }

    [HttpGet(Name = "listListingsForModeration")]
    [ProducesResponseType<ClassifiedsPage<ListingModerationItem>>(200)]
    public Task<ClassifiedsPage<ListingModerationItem>> ListAsync(CancellationToken cancellationToken, int page = 1) =>
        _publications.ListForModerationAsync(ActorId, SessionId, page, cancellationToken);

    [HttpPost("{id:guid}/block", Name = "blockListing")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> BlockAsync(Guid id, DecideListingReviewRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _publications.BlockAsync(ActorId, SessionId, id, request, cancellationToken);
        return NoContent();
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);
}
