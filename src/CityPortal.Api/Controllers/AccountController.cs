using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Authorize]
[Route("api/account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController : ControllerBase
{
    private readonly AccountOperations _accounts;

    public AccountController(AccountOperations accounts)
    {
        _accounts = accounts;
    }

    [HttpGet(Name = "getAccount")]
    [ProducesResponseType<AccountResponse>(200)]
    public async Task<ActionResult<AccountResponse>> GetAsync(CancellationToken cancellationToken)
    {
        AccountResponse? account = await _accounts.FindSessionAsync(Guid.Parse(User.FindFirstValue("session")!), cancellationToken);
        if (account is null)
            return Unauthorized();
        return account;
    }

    [HttpGet("email-confirmation", Name = "getConfirmationDelivery")]
    [ProducesResponseType<ConfirmationDeliveryResponse>(200)]
    public async Task<ActionResult<ConfirmationDeliveryResponse>> GetConfirmationAsync(CancellationToken cancellationToken)
    {
        AccountResponse? account = await _accounts.FindSessionAsync(Guid.Parse(User.FindFirstValue("session")!), cancellationToken);
        if (account is null)
            return Unauthorized();
        return account.ConfirmationDelivery;
    }

    [HttpPost("email-confirmation", Name = "resendConfirmation")]
    [ProducesResponseType<ConfirmationDeliveryResponse>(202)]
    public async Task<ActionResult<ConfirmationDeliveryResponse>> ResendAsync([FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        return StatusCode(202, await _accounts.ResendAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken));
    }
}
