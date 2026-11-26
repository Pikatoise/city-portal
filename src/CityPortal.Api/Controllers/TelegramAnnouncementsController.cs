using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Api.Contracts;
using CityPortal.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/editorial")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TelegramAnnouncementsController : ControllerBase
{
    private readonly TelegramWorkflow _telegram;

    public TelegramAnnouncementsController(TelegramWorkflow telegram)
    {
        _telegram = telegram;
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);

    [HttpGet("articles/{id:guid}/telegram", Name = "getTelegramAnnouncement")]
    public Task<TelegramAnnouncementResponse> GetAsync(Guid id, [FromQuery, MaxLength(33)] string? channel, CancellationToken cancellationToken) =>
        _telegram.GetAsync(ActorId, SessionId, id, channel, cancellationToken);

    [HttpGet("telegram/recovery", Name = "listTelegramRecovery")]
    public Task<TelegramRecoveryResponse[]> RecoveryAsync(CancellationToken cancellationToken) =>
        _telegram.RecoveryAsync(ActorId, SessionId, cancellationToken);

    [HttpPost("articles/{id:guid}/telegram", Name = "queueTelegramAnnouncement")]
    [Consumes("application/json")]
    [RequestSizeLimit(32768)]
    [ProducesResponseType<TelegramDeliveryResponse>(202)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<TelegramDeliveryResponse>> QueueAsync(Guid id, QueueTelegramAnnouncementRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        TelegramDeliveryResponse delivery = await _telegram.QueueAsync(ActorId, SessionId, id, request.RevisionId, request.Channel, request.Text, cancellationToken);
        return AcceptedAtRoute("getTelegramAnnouncement", new { id }, delivery);
    }

    [HttpPost("articles/{id:guid}/telegram/suggest", Name = "suggestTelegramAnnouncement")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<TelegramSuggestionResponse>(200)]
    [ProducesResponseType(409)]
    [ProducesResponseType(429)]
    [ProducesResponseType(503)]
    public async Task<TelegramSuggestionResponse> SuggestAsync(Guid id, SuggestTelegramAnnouncementRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        new(await _telegram.SuggestAsync(ActorId, SessionId, id, request.RevisionId, cancellationToken));

    [HttpPost("articles/{id:guid}/telegram/resolve", Name = "resolveTelegramAnnouncement")]
    [Consumes("application/json")]
    [RequestSizeLimit(32768)]
    [ProducesResponseType<TelegramDeliveryResponse>(200)]
    [ProducesResponseType(409)]
    public Task<TelegramDeliveryResponse> ResolveAsync(Guid id, ResolveTelegramAnnouncementRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        _telegram.ResolveAsync(ActorId, SessionId, id, request.Channel, request.Version, request.Retry, request.Cancel, request.PostUrl, request.RevisionId, request.Text, cancellationToken);
}
