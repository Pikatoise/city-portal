using System.Security.Claims;
using CityPortal.CitizenSubmissions;
using CityPortal.Identity;
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
