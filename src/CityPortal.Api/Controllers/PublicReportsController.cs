using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/reports")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicReportsController : ControllerBase
{
    private readonly ReportOperations _reports;

    public PublicReportsController(ReportOperations reports)
    {
        _reports = reports;
    }

    [HttpPost("{kind}/{id:guid}", Name = "submitContentReport")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> SubmitAsync(string kind, Guid id, SubmitContentReportRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        bool signedIn = User.Identity?.IsAuthenticated == true;
        Guid? actorId = signedIn ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!) : null;
        Guid? sessionId = signedIn ? Guid.Parse(User.FindFirstValue("session")!) : null;
        await _reports.SubmitAsync(kind, id, actorId, sessionId, request, cancellationToken);
        return NoContent();
    }
}
