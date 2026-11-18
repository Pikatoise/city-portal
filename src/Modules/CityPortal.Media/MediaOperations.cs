using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

public sealed class MediaOperations
{
    private readonly MediaDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly MediaImageProcessor _processor;
    private readonly MediaObjectStore _storage;
    private readonly TimeProvider _time;

    internal MediaOperations(MediaDbContext context, AccountAuthorization authorization, MediaImageProcessor processor, MediaObjectStore storage, TimeProvider time)
    {
        _context = context;
        _authorization = authorization;
        _processor = processor;
        _storage = storage;
        _time = time;
    }

    public Task<ImageResponse> UploadAsync(Guid actorId, Guid sessionId, string name, string contentType, Stream input, CancellationToken cancellationToken) =>
        UploadAsync(actorId, sessionId, name, contentType, input, forSuggestion: false, cancellationToken);

    public Task<ImageResponse> UploadSuggestionAsync(Guid actorId, Guid sessionId, string name, string contentType, Stream input, CancellationToken cancellationToken) =>
        UploadAsync(actorId, sessionId, name, contentType, input, forSuggestion: true, cancellationToken);

    private async Task<ImageResponse> UploadAsync(Guid actorId, Guid sessionId, string name, string contentType, Stream input,
        bool forSuggestion, CancellationToken cancellationToken)
    {
        string[] roles = forSuggestion ? ["RegularUser"] : ["Author"];
        await _authorization.RequireAsync(actorId, sessionId, roles, cancellationToken);
        string filename = Path.GetFileName(name.Replace('\\', '/')).Trim();
        if (filename.Length is < 1 or > 200 || filename.Contains('\0'))
            throw new AccountOperationException(400, "image_name", "The file name must contain 1 to 200 characters.");
        IReadOnlyList<ProcessedImage> outputs = await _processor.ProcessAsync(input, contentType, cancellationToken);
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, roles, cancellationToken);
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        var image = new MediaImage { Id = Guid.NewGuid(), OwnerId = actorId, Name = filename, Width = outputs[0].Width, Height = outputs[0].Height, CreatedAtUtc = _time.GetUtcNow(), NextAttemptAtUtc = _time.GetUtcNow(), ForSuggestion = forSuggestion };
        context.Images.Add(image);
        context.Outputs.AddRange(outputs.Select(output => new ImageOutput { ImageId = image.Id, Variant = output.Variant, Width = output.Width, Height = output.Height, PendingBytes = output.Bytes }));
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(image);
    }

    public async Task<MediaPage<ImageResponse>> ListAsync(Guid actorId, Guid sessionId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Author"], cancellationToken);
        MediaDatabase.Page(page, pageSize);
        IQueryable<MediaImage> query = _context.Images.AsNoTracking().Where(image => image.OwnerId == actorId && !image.ForSuggestion && image.State != "Deleting");
        int total = await query.CountAsync(cancellationToken);
        MediaImage[] images = await query.OrderByDescending(image => image.CreatedAtUtc).ThenBy(image => image.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new MediaPage<ImageResponse>(images.Select(Response).ToArray(), total, page, pageSize);
    }

    public async Task<ImageResponse> GetAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, ["Author", "Editor", "Moderator", "Administrator"], cancellationToken);
        MediaImage image = await _context.Images.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.State != "Deleting", cancellationToken) ?? throw MediaDatabase.Missing();
        if (!await ProtectedAccessAsync(image, actor, cancellationToken))
            throw MediaDatabase.Missing();
        return Response(image);
    }

    public async Task<Stream> OpenAsync(Guid? actorId, Guid? sessionId, Guid id, string variant, CancellationToken cancellationToken)
    {
        if (!new[] { "master", "480", "1280", "2560" }.Contains(variant, StringComparer.Ordinal))
            throw MediaDatabase.Missing();
        MediaImage image = await _context.Images.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.State == "Ready", cancellationToken) ?? throw MediaDatabase.Missing();
        bool blocked = await _context.Templates.AnyAsync(template => template.ImageId == id && template.Blocked, cancellationToken);
        bool isPublic = !blocked && await _context.References.AnyAsync(reference => reference.ImageId == id && reference.Public, cancellationToken);
        if (!isPublic)
        {
            if (actorId is null || sessionId is null)
                throw MediaDatabase.Missing();
            AccountResponse actor;
            try
            {
                actor = await _authorization.RequireAsync(actorId.Value, sessionId.Value, ["Author", "Editor", "Moderator", "Administrator"], cancellationToken);
            }
            catch (AccountOperationException exception) when (exception.Status is 401 or 403)
            {
                throw MediaDatabase.Missing();
            }
            bool moderator = actor.Roles.Contains("Moderator") || actor.Roles.Contains("Administrator");
            if (blocked && !moderator || !await ProtectedAccessAsync(image, actor, cancellationToken))
                throw MediaDatabase.Missing();
        }
        return await _storage.OpenAsync(id, variant, cancellationToken);
    }

    private async Task<bool> ProtectedAccessAsync(MediaImage image, AccountResponse actor, CancellationToken cancellationToken)
    {
        bool author = actor.Roles.Contains("Author");
        bool administrator = actor.Roles.Contains("Administrator");
        if (author && image.OwnerId == actor.Id && !image.ForSuggestion)
            return true;
        if ((administrator || actor.Roles.Contains("Moderator")) && await _context.Events.AnyAsync(entry => entry.ImageId == image.Id, cancellationToken))
            return true;
        if (author && await _context.Templates.AnyAsync(template => template.ImageId == image.Id && template.Approval == "Approved" && !template.Archived && !template.Blocked, cancellationToken))
            return true;
        return await _context.References.AnyAsync(reference => reference.ImageId == image.Id &&
            (author && reference.AuthorId == actor.Id && reference.AuthorVisible
                || reference.Submitted && (administrator || actor.Roles.Contains("Editor") && reference.ReviewerId == actor.Id)), cancellationToken);
    }

    internal static ImageResponse Response(MediaImage image) => new(image.Id, image.Name, image.State, image.Width, image.Height, image.Version, image.CreatedAtUtc, image.ErrorCode);
}
