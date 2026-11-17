using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CityPortal.Api.Contracts;
using CityPortal.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Authorize]
[Route("api/media")]
public sealed class MediaController : ControllerBase
{
    private readonly MediaOperations _images;
    private readonly TemplateOperations _templates;

    public MediaController(MediaOperations images, TemplateOperations templates)
    {
        _images = images;
        _templates = templates;
    }

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid SessionId => Guid.Parse(User.FindFirstValue("session")!);

    [HttpPost("images", Name = "uploadImage")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024, MemoryBufferThreshold = 11 * 1024 * 1024)]
    [ProducesResponseType<ImageResponse>(202)]
    public async Task<ActionResult<ImageResponse>> UploadImageAsync([Required] IFormFile file, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        if (file.Length > MediaImageProcessor.MaximumBytes)
            return Problem(statusCode: 413, title: "Images must not exceed 10 MiB.");
        await using Stream input = file.OpenReadStream();
        ImageResponse image = await _images.UploadAsync(ActorId, SessionId, file.FileName, file.ContentType, input, cancellationToken);
        return Accepted($"/api/media/images/{image.Id}", image);
    }

    [HttpGet("images", Name = "listImages")]
    public Task<MediaPage<ImageResponse>> ListImagesAsync(CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
        _images.ListAsync(ActorId, SessionId, page, pageSize, cancellationToken);

    [HttpGet("images/{id:guid}", Name = "getImage")]
    public Task<ImageResponse> GetImageAsync(Guid id, CancellationToken cancellationToken) => _images.GetAsync(ActorId, SessionId, id, cancellationToken);

    [AllowAnonymous]
    [HttpGet("images/{id:guid}/{variant}", Name = "openImage")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> OpenImageAsync(Guid id, string variant, CancellationToken cancellationToken)
    {
        Guid? actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid actorId) ? actorId : null;
        Guid? session = Guid.TryParse(User.FindFirstValue("session"), out Guid sessionId) ? sessionId : null;
        Stream stream = await _images.OpenAsync(actor, session, id, variant, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, "image/webp", enableRangeProcessing: false);
    }

    [HttpGet("templates", Name = "listTemplates")]
    public Task<MediaPage<TemplateResponse>> ListTemplatesAsync(CancellationToken cancellationToken, string scope = "library", int page = 1, int pageSize = 20) =>
        _templates.ListAsync(ActorId, SessionId, scope, page, pageSize, cancellationToken);

    [HttpGet("templates/{id:guid}", Name = "getTemplate")]
    public Task<TemplateResponse> GetTemplateAsync(Guid id, CancellationToken cancellationToken) => _templates.GetAsync(ActorId, SessionId, id, cancellationToken);

    [HttpPost("templates", Name = "createTemplate")]
    [ProducesResponseType<TemplateResponse>(201)]
    public async Task<ActionResult<TemplateResponse>> CreateTemplateAsync(CreateTemplateRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        TemplateResponse template = await _templates.CreateAsync(ActorId, SessionId, request.ImageId, request.Name, cancellationToken);
        return Created($"/api/media/templates/{template.Id}", template);
    }

    [HttpPost("templates/{id:guid}/{operation:regex(^(submit|approve|reject|archive|restore|block|unblock)$)}", Name = "templateAction")]
    public Task<TemplateResponse> TemplateActionAsync(Guid id, string operation, TemplateActionRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        _templates.ActAsync(ActorId, SessionId, id, operation, request.Version, request.Reason, request.ImageId, request.Name, cancellationToken);

    [HttpGet("template-review-queue", Name = "templateQueue")]
    public Task<MediaPage<TemplateResponse>> TemplateQueueAsync(CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
        _templates.ListAsync(ActorId, SessionId, "queue", page, pageSize, cancellationToken);

    [HttpGet("templates/{id:guid}/history", Name = "templateHistory")]
    public Task<MediaPage<TemplateHistoryResponse>> TemplateHistoryAsync(Guid id, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
        _templates.HistoryAsync(ActorId, SessionId, id, page, pageSize, cancellationToken);

    [HttpPost("templates/{id:guid}/removal-requests", Name = "requestTemplateRemoval")]
    [ProducesResponseType<RemovalRequestResponse>(200)]
    public Task<RemovalRequestResponse> RequestTemplateRemovalAsync(Guid id, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        _templates.RequestRemovalAsync(ActorId, SessionId, id, cancellationToken);

    [HttpGet("template-removal-requests", Name = "templateRemovalQueue")]
    public Task<MediaPage<RemovalRequestResponse>> TemplateRemovalQueueAsync(CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
        _templates.RemovalQueueAsync(ActorId, SessionId, page, pageSize, cancellationToken);

    [HttpPost("template-removal-requests/{id:guid}/resolve", Name = "resolveTemplateRemoval")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResolveTemplateRemovalAsync(Guid id, MediaVersionRequest request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        await _templates.ResolveRemovalAsync(ActorId, SessionId, id, request.Version, cancellationToken);
        return NoContent();
    }
}
