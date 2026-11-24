using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Classifieds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/staff/classifieds/review")]
[Produces("application/json")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassifiedsReviewController : ControllerBase
{
    private readonly ListingReviewOperations _reviews;

    public ClassifiedsReviewController(ListingReviewOperations reviews)
    {
        _reviews = reviews;
    }

    [HttpGet(Name = "listListingReviews")]
    [ProducesResponseType<ClassifiedsPage<ListingReviewQueueItem>>(200)]
    [ProducesResponseType(400)]
    public Task<ClassifiedsPage<ListingReviewQueueItem>> ListAsync(CancellationToken cancellationToken, int page = 1) =>
        _reviews.ListAsync(ActorId, SessionId, page, cancellationToken);

    [HttpGet("{id:guid}", Name = "getListingReview")]
    [ProducesResponseType<ListingReviewPreview>(200)]
    [ProducesResponseType(404)]
    public Task<ListingReviewPreview> GetAsync(Guid id, CancellationToken cancellationToken) =>
        _reviews.GetAsync(ActorId, SessionId, id, cancellationToken);

    [HttpGet("{id:guid}/images/{imageId:guid}", Name = "openListingReviewPhoto")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> OpenImageAsync(Guid id, Guid imageId, [FromQuery, BindRequired, Range(1, int.MaxValue)] int version,
        CancellationToken cancellationToken)
    {
        Stream image = await _reviews.OpenImageAsync(ActorId, SessionId, id, imageId, version, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image, "image/webp");
    }

    [HttpPost("{id:guid}/return", Name = "returnListingReview")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ReturnAsync(Guid id, DecideListingReviewRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _reviews.DecideAsync(ActorId, SessionId, id, request, reject: false, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject", Name = "rejectListingReview")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RejectAsync(Guid id, DecideListingReviewRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _reviews.DecideAsync(ActorId, SessionId, id, request, reject: true, cancellationToken);
        return NoContent();
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);
}
