using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Comments;
using CityPortal.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/staff/comments")]
[Produces("application/json")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffCommentsController : ControllerBase
{
    private readonly ReportOperations _reports;

    public StaffCommentsController(ReportOperations reports)
    {
        _reports = reports;
    }

    [HttpPost("{id:guid}/hide", Name = "hidePublicComment")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> HideAsync(Guid id, HideCommentRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid actorId)
            || !Guid.TryParse(User.FindFirstValue("session"), out Guid sessionId))
            return Unauthorized();
        await _reports.HideCommentAsync(id, actorId, sessionId, request.Reason, cancellationToken);
        return NoContent();
    }
}
