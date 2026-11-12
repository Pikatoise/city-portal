using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Api.Contracts;
using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdministrationController : ControllerBase
{
    private readonly AccountAdministration _administration;

    public AdministrationController(AccountAdministration administration)
    {
        _administration = administration;
    }

    // AccountSecurity rejects cookies without a valid persisted session and replaces both identifier claims.
    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);

    [HttpGet("accounts", Name = "listAccounts")]
    [ProducesResponseType<AccountPage>(200)]
    public Task<AccountPage> ListAsync([FromQuery] string? query, [FromQuery] string? role, [FromQuery] bool? emailConfirmed, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _administration.ListAsync(ActorId, SessionId, query, role, emailConfirmed, page, pageSize, cancellationToken);

    [HttpGet("accounts/{id:guid}", Name = "getAdminAccount")]
    [ProducesResponseType<AccountSummary>(200)]
    public Task<AccountSummary> GetAsync(Guid id, CancellationToken cancellationToken)
        => _administration.GetAsync(ActorId, SessionId, id, cancellationToken);

    [HttpPut("accounts/{id:guid}/roles", Name = "changeRoles")]
    [Consumes("application/json")]
    [ProducesResponseType<AccountSummary>(200)]
    public Task<AccountSummary> ChangeRolesAsync(Guid id, ChangeRolesRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
        => _administration.ChangeRolesAsync(ActorId, SessionId, id, request.Roles, request.Version, cancellationToken);

    [HttpGet("accounts/{id:guid}/role-history", Name = "getRoleHistory")]
    [ProducesResponseType<RoleHistoryPage>(200)]
    public Task<RoleHistoryPage> HistoryAsync(Guid id, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _administration.HistoryAsync(ActorId, SessionId, id, page, pageSize, cancellationToken);

    [HttpGet("email-deliveries", Name = "listEmailDeliveries")]
    [ProducesResponseType<EmailDeliveryPage>(200)]
    public Task<EmailDeliveryPage> DeliveriesAsync([FromQuery] string? state, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => _administration.DeliveriesAsync(ActorId, SessionId, state, page, pageSize, cancellationToken);

    [HttpPost("email-deliveries/{id:guid}/replay", Name = "replayEmail")]
    [Consumes("application/json")]
    [ProducesResponseType(202)]
    public async Task<IActionResult> ReplayAsync(Guid id, ReplayEmailRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        await _administration.ReplayAsync(ActorId, SessionId, id, request.Version, cancellationToken);
        return Accepted();
    }
}
