using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Moderator,Administrator")]
[Route("api/staff/reports")]
[Produces("application/json")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffReportsController : ControllerBase
{
    private readonly ReportOperations _reports;

    public StaffReportsController(ReportOperations reports)
    {
        _reports = reports;
    }

    [HttpGet("{kind}", Name = "listReportedContent")]
    [ProducesResponseType<ReportQueuePage>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<ReportQueuePage> ListAsync(string kind, CancellationToken cancellationToken, int page = 1, bool history = false) =>
        _reports.ListAsync(kind, ActorId, SessionId, page, history, cancellationToken);

    [HttpGet("{kind}/{id:guid}", Name = "getReportReview")]
    [ProducesResponseType<ReportReviewPreview>(200)]
    [ProducesResponseType(404)]
    public Task<ReportReviewPreview> GetAsync(string kind, Guid id, CancellationToken cancellationToken) =>
        _reports.GetAsync(kind, id, ActorId, SessionId, cancellationToken);

    [HttpPost("{kind}/{id:guid}/decide", Name = "decideContentReports")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> DecideAsync(string kind, Guid id, DecideContentReportsRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        await _reports.DecideAsync(kind, id, ActorId, SessionId, request, cancellationToken);
        return NoContent();
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);
}
