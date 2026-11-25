using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Api.Contracts;
using CityPortal.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/staff/account-blocks")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountBlockingController : ControllerBase
{
    private readonly AccountBlocking _blocking;

    public AccountBlockingController(AccountBlocking blocking)
    {
        _blocking = blocking;
    }

    [HttpGet(Name = "listAccountsForBlocking")]
    [ProducesResponseType<AccountBlockPage>(200)]
    public Task<AccountBlockPage> ListAsync(string? query, CancellationToken cancellationToken, int page = 1) =>
        _blocking.ListAsync(ActorId, SessionId, query, page, cancellationToken);

    [HttpGet("{id:guid}/history", Name = "getAccountBlockHistory")]
    [ProducesResponseType<AccountBlockHistory>(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public Task<AccountBlockHistory> HistoryAsync(Guid id, CancellationToken cancellationToken, [Range(1, 100000)] int page = 1) =>
        _blocking.ReadStaffHistoryAsync(ActorId, SessionId, id, page, cancellationToken);

    [HttpPost("{id:guid}/block", Name = "blockAccount")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> BlockAsync(Guid id, BlockAccountRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _blocking.ChangeAsync(ActorId, SessionId, id, request.Version, request.Reason, block: true, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/unblock", Name = "unblockAccount")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> UnblockAsync(Guid id, UnblockAccountRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _blocking.ChangeAsync(ActorId, SessionId, id, request.Version, null, block: false, cancellationToken);
        return NoContent();
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);
}
