using CityPortal.Api.Contracts;
using CityPortal.Identity;
using CityPortal.Integrations;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemController : ControllerBase
{
    private readonly TimeZoneInfo _timeZone;
    private readonly SiteInstallation _installation;
    private readonly PublicIntegrationReads _integrations;

    public SystemController(TimeZoneInfo timeZone, SiteInstallation installation, PublicIntegrationReads integrations)
    {
        _timeZone = timeZone;
        _installation = installation;
        _integrations = integrations;
    }

    /// <summary>Returns public application metadata.</summary>
    [HttpGet("info", Name = "getSystemInfo")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<SystemInfoResponse>> GetInfoAsync(CancellationToken cancellationToken)
    {
        InstallationInfo site = await _installation.GetAsync(cancellationToken);
        return Ok(new SystemInfoResponse(site.Name ?? "CityPortal", 1)
        {
            TimeZone = site.TimeZone ?? _timeZone.Id,
            City = site.City ?? "",
            Region = site.Region ?? "",
            RssEnabled = site.Installed && await _integrations.IsRssEnabledAsync(cancellationToken)
        });
    }
}
