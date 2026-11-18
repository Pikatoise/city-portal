using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.CitizenSubmissions;
using CityPortal.Identity;
using CityPortal.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/account/suggestions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SuggestionsController : ControllerBase
{
    private readonly SuggestionOperations _suggestions;

    public SuggestionsController(SuggestionOperations suggestions)
    {
        _suggestions = suggestions;
    }

    [HttpPost("uploads", Name = "uploadSuggestionImage")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024, MemoryBufferThreshold = 11 * 1024 * 1024)]
    [ProducesResponseType<ImageResponse>(202)]
    public async Task<ActionResult<ImageResponse>> UploadAsync([FromServices] MediaOperations media, [Required] IFormFile file,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        if (file.Length > MediaImageProcessor.MaximumBytes)
            return Problem(statusCode: 413, title: "Images must not exceed 10 MiB.");
        await using Stream input = file.OpenReadStream();
        var sessionId = Guid.Parse(User.FindFirstValue("session")!);
        return Accepted(await media.UploadSuggestionAsync(ResidentId(requireConfirmation: true), sessionId, file.FileName,
            file.ContentType, input, cancellationToken));
    }

    [HttpGet("{id:guid}/images/{imageId:guid}", Name = "openMySuggestionImage")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> OpenImageAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        Stream image = await _suggestions.OpenImageAsync(ResidentId(), id, imageId, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image, "image/webp");
    }

    [HttpGet(Name = "listMySuggestions")]
    public Task<SuggestionPage> ListAsync(CancellationToken cancellationToken, int page = 1) =>
        _suggestions.ListAsync(ResidentId(), page, cancellationToken);

    [HttpGet("{id:guid}", Name = "getMySuggestion")]
    [ProducesResponseType<SuggestionResponse>(200)]
    [ProducesResponseType(404)]
    public Task<SuggestionResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        _suggestions.GetAsync(ResidentId(), id, cancellationToken);

    [HttpPost(Name = "createSuggestion")]
    [RequestSizeLimit(65536)]
    [ProducesResponseType<SuggestionResponse>(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<SuggestionResponse>> CreateAsync(SuggestionInput input,
        [FromHeader(Name = "X-CSRF-TOKEN")] string csrf, CancellationToken cancellationToken)
    {
        SuggestionResponse item = await _suggestions.CreateAsync(ResidentId(requireConfirmation: true), input, cancellationToken);
        return CreatedAtRoute("getMySuggestion", new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}", Name = "updateMySuggestion")]
    [RequestSizeLimit(65536)]
    [ProducesResponseType<SuggestionResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<SuggestionResponse> UpdateAsync(Guid id, UpdateSuggestionRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string csrf, CancellationToken cancellationToken) =>
        _suggestions.UpdateAsync(ResidentId(requireConfirmation: true), id, request, cancellationToken);

    private Guid ResidentId(bool requireConfirmation = false)
    {
        if (requireConfirmation && User.FindFirstValue("email_confirmed") != bool.TrueString)
            throw new AccountOperationException(403, "email_confirmation_required", "Confirm your Email before submitting suggestions.");
        return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new AccountOperationException(401, "invalid_session", "Log in again."));
    }
}
