using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Classifieds;
using CityPortal.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/classifieds/catalog")]
[Produces("application/json")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassifiedsCatalogController : ControllerBase
{
    private readonly ClassifiedsCatalog _catalog;

    public ClassifiedsCatalogController(ClassifiedsCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("{kind}", Name = "listAdminListingCatalog")]
    [ProducesResponseType<ClassifiedsPage<CatalogEntryResponse>>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public Task<ClassifiedsPage<CatalogEntryResponse>> ListAsync(string kind, CancellationToken cancellationToken, int page = 1)
    {
        if (User.FindFirstValue("email_confirmed") != bool.TrueString)
            throw new AccountOperationException(403, "email_confirmation_required", "Confirm your Email before managing the catalog.");
        return _catalog.ListAsync(kind, includeArchived: true, page, cancellationToken);
    }

    [HttpPost("{kind}", Name = "createListingCatalogEntry")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<CatalogEntryResponse>(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<CatalogEntryResponse>> CreateAsync(string kind, SaveClassifiedsCatalogEntryRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken)
    {
        CatalogEntryResponse entry = await _catalog.SaveAsync(ActorId, SessionId, kind, null, 0, request, cancellationToken);
        return StatusCode(201, entry);
    }

    [HttpPut("{kind}/{id:guid}", Name = "saveListingCatalogEntry")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<CatalogEntryResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public Task<CatalogEntryResponse> UpdateAsync(string kind, Guid id, UpdateClassifiedsCatalogEntryRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrf, CancellationToken cancellationToken) =>
        _catalog.SaveAsync(ActorId, SessionId, kind, id, request.Version, request.Entry, cancellationToken);

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);
}
