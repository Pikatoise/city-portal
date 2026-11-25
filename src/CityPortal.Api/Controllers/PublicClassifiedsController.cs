using CityPortal.Classifieds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/classifieds")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicClassifiedsController : ControllerBase
{
    private readonly ListingPublications _publications;

    public PublicClassifiedsController(ListingPublications publications)
    {
        _publications = publications;
    }

    [HttpGet(Name = "listPublicListings")]
    [ProducesResponseType<ClassifiedsPage<PublicListingSummary>>(200)]
    [ProducesResponseType(400)]
    public Task<ClassifiedsPage<PublicListingSummary>> ListAsync([FromQuery(Name = "")] PublicListingsQuery request,
        [FromServices] PublicListingSearch search, CancellationToken cancellationToken) => search.ListAsync(request, cancellationToken);

    [HttpGet("filters", Name = "getPublicListingFilters")]
    [ProducesResponseType<PublicListingFilters>(200)]
    public Task<PublicListingFilters> FiltersAsync([FromServices] PublicListingSearch search, CancellationToken cancellationToken) =>
        search.FiltersAsync(cancellationToken);

    [HttpGet("{id:guid}", Name = "getPublicListing")]
    [ProducesResponseType<PublicListingResponse>(200)]
    [ProducesResponseType(404)]
    public Task<PublicListingResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        _publications.GetAsync(id, cancellationToken);

    [HttpGet("{id:guid}/images/{imageId:guid}", Name = "openPublicListingPhoto")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> OpenPhotoAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        Stream image = await _publications.OpenImageAsync(id, imageId, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image, "image/webp");
    }
}
