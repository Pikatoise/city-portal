using System.ComponentModel.DataAnnotations;
using CityPortal.Api.Contracts;
using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Route("api/installation")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InstallationController : ControllerBase
{
    private readonly SiteInstallation _installation;
    private readonly IConfiguration _configuration;

    public InstallationController(SiteInstallation installation, IConfiguration configuration)
    {
        _installation = installation;
        _configuration = configuration;
    }

    [HttpGet("status", Name = "getInstallationStatus")]
    [ProducesResponseType<InstallationStatusResponse>(200)]
    public async Task<ActionResult<InstallationStatusResponse>> StatusAsync(CancellationToken cancellationToken)
    {
        bool installed = (await _installation.GetAsync(cancellationToken)).Installed;
        return new InstallationStatusResponse(installed, !installed && _configuration.GetValue<bool>("Installation:Enabled"));
    }

    [HttpPost("complete", Name = "installSite")]
    [Consumes("application/json")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> CompleteAsync(InstallSiteRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue<bool>("Installation:Enabled"))
            throw new AccountOperationException(403, "installation_disabled", "Setup is disabled. Complete installation in local mode before opening network access.");
        if (Uri.CheckHostName(request.SmtpHost) == UriHostNameType.Unknown)
            throw new AccountOperationException(400, "invalid_smtp_host", "Provide a valid SMTP hostname or IP address.");
        if (request.ImplicitTls && !request.UseTls)
            throw new AccountOperationException(400, "invalid_smtp_security", "Implicit TLS requires TLS to be enabled.");
        string origin = _configuration["Mail:PublicOrigin"] ?? "https://localhost:8443";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? publicUri) || publicUri.Scheme != "https" || publicUri.AbsolutePath != "/" || publicUri.UserInfo.Length > 0 || publicUri.Query.Length > 0 || publicUri.Fragment.Length > 0)
            throw new AccountOperationException(503, "invalid_public_origin", "Configure the site's public HTTPS origin on the server.");
        try
        {
            await _installation.CompleteAsync(request.Email, request.DisplayName, request.Password, request.Name, request.City, request.Region ?? "", request.TimeZone,
                new AccountMailOptions
                {
                    PublicOrigin = origin,
                    SmtpHost = request.SmtpHost,
                    SmtpPort = request.SmtpPort,
                    SenderAddress = request.SenderAddress,
                    Username = request.SmtpUsername,
                    Password = request.SmtpPassword,
                    UseTls = request.UseTls,
                    ImplicitTls = request.ImplicitTls
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or MailKit.Net.Smtp.SmtpProtocolException or MailKit.Net.Smtp.SmtpCommandException
            or MailKit.Security.AuthenticationException or System.Security.Authentication.AuthenticationException or System.Net.Sockets.SocketException
            or TimeoutException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AccountOperationException(503, "smtp_test_failed", "The SMTP test failed. Check the mail settings and try again.");
        }
        return NoContent();
    }
}
