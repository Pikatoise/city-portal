using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/integrations")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntegrationAdministrationController : ControllerBase
{
    private readonly IntegrationAdministration _administration;

    public IntegrationAdministrationController(IntegrationAdministration administration)
    {
        _administration = administration;
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);

    [HttpGet(Name = "getIntegrationSettings")]
    [ProducesResponseType<IntegrationStatus>(200)]
    public Task<IntegrationStatus> GetAsync(CancellationToken cancellationToken) =>
        _administration.GetAsync(ActorId, SessionId, cancellationToken);

    [HttpPut(Name = "saveIntegrationSettings")]
    [Consumes("application/json")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<IntegrationStatus>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public Task<IntegrationStatus> SaveAsync(SaveIntegrationSettings request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken,
        CancellationToken cancellationToken) => _administration.SaveAsync(ActorId, SessionId, request, cancellationToken);
}
