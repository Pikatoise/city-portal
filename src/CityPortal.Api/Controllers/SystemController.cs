using CityPortal.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemController : ControllerBase
{
    private readonly TimeZoneInfo _timeZone;

    public SystemController(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    /// <summary>Returns public application metadata.</summary>
    [HttpGet("info", Name = "getSystemInfo")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo() => Ok(new SystemInfoResponse("CityPortal", 1) { TimeZone = _timeZone.Id });
}
