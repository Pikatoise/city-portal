using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Api.Contracts;
using CityPortal.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController : ControllerBase
{
    private readonly AccountOperations _accounts;
    private readonly IAntiforgery _csrf;

    public AuthController(AccountOperations accounts, IAntiforgery csrf)
    {
        _accounts = accounts;
        _csrf = csrf;
    }

    [HttpGet("csrf", Name = "getCsrf")]
    [ProducesResponseType<CsrfResponse>(200)]
    public ActionResult<CsrfResponse> GetCsrf() => new CsrfResponse { Token = _csrf.GetAndStoreTokens(HttpContext).RequestToken! };

    [HttpPost("register", Name = "register")]
    [Consumes("application/json")]
    [ProducesResponseType<AccountResponse>(201)]
    public async Task<ActionResult<AccountResponse>> RegisterAsync(RegisterRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        AccountResponse account = await _accounts.RegisterAsync(request.Email, request.DisplayName, request.Password, cancellationToken);
        return StatusCode(201, account);
    }

    [HttpPost("login", Name = "login")]
    [Consumes("application/json")]
    [ProducesResponseType<AccountResponse>(200)]
    public async Task<ActionResult<AccountResponse>> LoginAsync(LoginRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        (AccountResponse account, Guid sessionId, DateTimeOffset expires) = await _accounts.LoginAsync(request.Email, request.Password, request.RememberMe, cancellationToken);
        var identity = new ClaimsIdentity([new Claim("session", sessionId.ToString()), new Claim(ClaimTypes.NameIdentifier, account.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = expires,
            AllowRefresh = false
        });
        return account;
    }

    [Authorize]
    [HttpPost("logout", Name = "logout")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> LogoutAsync([FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        await _accounts.LogoutAsync(Guid.Parse(User.FindFirstValue("session")!), cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpPost("confirm-email", Name = "confirmEmail")]
    [Consumes("application/json")]
    [ProducesResponseType<ConfirmEmailResponse>(200)]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmAsync(ConfirmEmailRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
        => new ConfirmEmailResponse { Status = await _accounts.ConfirmAsync(request.LinkId, request.Token, cancellationToken) };

}
