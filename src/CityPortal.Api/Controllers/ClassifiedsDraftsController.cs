using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Classifieds;
using CityPortal.Identity;
using CityPortal.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "RegularUser")]
[Route("api/account/classifieds")]
[Produces("application/json")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassifiedsDraftsController : ControllerBase
{
    private readonly ListingDraftOperations _drafts;
    private readonly ClassifiedsCatalog _catalog;

    public ClassifiedsDraftsController(ListingDraftOperations drafts, ClassifiedsCatalog catalog)
    {
        _drafts = drafts;
        _catalog = catalog;
    }

    [HttpGet("catalog/{kind}", Name = "listListingCatalog")]
    [ProducesResponseType<ClassifiedsPage<CatalogEntryResponse>>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<ClassifiedsPage<CatalogEntryResponse>> CatalogAsync(string kind, CancellationToken cancellationToken, int page = 1)
    {
        RequireOwner();
        return _catalog.ListAsync(kind, includeArchived: false, page, cancellationToken);
    }

    [HttpGet("drafts", Name = "listMyListingDrafts")]
    [ProducesResponseType<ClassifiedsPage<ListingDraftResponse>>(200)]
    [ProducesResponseType(400)]
    public Task<ClassifiedsPage<ListingDraftResponse>> ListAsync(CancellationToken cancellationToken, int page = 1) =>
        _drafts.ListAsync(RequireOwner(), page, cancellationToken);

    [HttpGet("drafts/{id:guid}", Name = "getMyListingDraft")]
    [ProducesResponseType<ListingDraftResponse>(200)]
    [ProducesResponseType(404)]
    public Task<ListingDraftResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        _drafts.GetAsync(RequireOwner(), id, cancellationToken);

    [HttpPost("uploads", Name = "uploadListingPhoto")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024, MemoryBufferThreshold = 11 * 1024 * 1024)]
    [ProducesResponseType<ImageResponse>(202)]
    [ProducesResponseType(400)]
    [ProducesResponseType(413)]
    [ProducesResponseType(429)]
    public async Task<ActionResult<ImageResponse>> UploadAsync([FromServices] MediaOperations media, [Required] IFormFile file,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        if (file.Length > MediaImageProcessor.MaximumBytes)
            return Problem(statusCode: 413, title: "Images must not exceed 10 MiB.");
        await using Stream input = file.OpenReadStream();
        return Accepted(await media.UploadListingAsync(RequireOwner(), SessionId, file.FileName, file.ContentType, input, cancellationToken));
    }

    [HttpGet("drafts/{id:guid}/images/{imageId:guid}", Name = "openMyListingPhoto")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> OpenImageAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        Stream image = await _drafts.OpenImageAsync(RequireOwner(), id, imageId, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image, "image/webp");
    }

    [HttpPost("drafts", Name = "createListingDraft")]
    [Consumes("application/json")]
    [RequestSizeLimit(65536)]
    [ProducesResponseType<ListingDraftResponse>(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ListingDraftResponse>> CreateAsync(ListingDraftInput input,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        ListingDraftResponse draft = await _drafts.SaveAsync(RequireOwner(), SessionId, null, 0, input, cancellationToken);
        return CreatedAtRoute("getMyListingDraft", new { id = draft.Id }, draft);
    }

    [HttpPut("drafts/{id:guid}", Name = "saveListingDraft")]
    [Consumes("application/json")]
    [RequestSizeLimit(65536)]
    [ProducesResponseType<ListingDraftResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<ListingDraftResponse> UpdateAsync(Guid id, UpdateListingDraftRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken) =>
        _drafts.SaveAsync(RequireOwner(), SessionId, id, request.Version, request.Details, cancellationToken);

    [HttpPost("drafts/{id:guid}/submit", Name = "submitListingDraft")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ListingDraftResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<ListingDraftResponse> SubmitAsync(Guid id, ListingDraftVersionRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken) =>
        _drafts.SubmitAsync(RequireOwner(), SessionId, id, request.Version, cancellationToken);

    [HttpPost("drafts/{id:guid}/recall", Name = "recallListingDraft")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ListingDraftResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<ListingDraftResponse> RecallAsync(Guid id, ListingDraftVersionRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken) =>
        _drafts.RecallAsync(RequireOwner(), SessionId, id, request.Version, cancellationToken);

    [HttpDelete("drafts/{id:guid}", Name = "deleteListingDraft")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> DeleteAsync(Guid id, [FromQuery, BindRequired, Range(1, int.MaxValue)] int version,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _drafts.DeleteAsync(RequireOwner(), SessionId, id, version, cancellationToken);
        return NoContent();
    }

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);

    private Guid RequireOwner()
    {
        if (User.FindFirstValue("email_confirmed") != bool.TrueString)
            throw new AccountOperationException(403, "email_confirmation_required", "Confirm your Email before managing listings.");
        return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new AccountOperationException(401, "invalid_session", "Log in again."));
    }
}
