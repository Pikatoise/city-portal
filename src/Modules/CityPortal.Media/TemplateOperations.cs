using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

public sealed class TemplateOperations
{
    private readonly MediaDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;
    private readonly TemplateNotifications _notifications;
    private static readonly string[] _readRoles = ["Author", "Moderator", "Administrator"];
    private static readonly string[] _moderationRoles = ["Moderator", "Administrator"];

    internal TemplateOperations(MediaDbContext context, AccountAuthorization authorization, TimeProvider time, TemplateNotifications notifications)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
        _notifications = notifications;
    }

    public async Task<MediaPage<TemplateResponse>> ListAsync(Guid actorId, Guid sessionId, string scope, int page, int pageSize, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, scope is "queue" or "moderation" ? _moderationRoles : ["Author"], cancellationToken, allowBlockedOwnerRead: scope == "mine");
        MediaDatabase.Page(page, pageSize);
        IQueryable<ImageTemplate> query = _context.Templates.AsNoTracking();
        query = scope switch
        {
            "mine" => query.Where(template => template.OwnerId == actorId),
            "library" => query.Where(template => template.Approval == "Approved" && !template.Archived && !template.Blocked && !_context.Templates.Any(other => other.ImageId == template.ImageId && other.Blocked)),
            "queue" => query.Where(template => template.Approval == "Pending" && template.Submitted),
            "moderation" => query,
            _ => throw new AccountOperationException(400, "invalid_scope", "Choose library, mine, or an authorized moderation queue.")
        };
        int total = await query.CountAsync(cancellationToken);
        ImageTemplate[] items = await query.OrderByDescending(template => template.CreatedAtUtc).ThenBy(template => template.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new MediaPage<TemplateResponse>(items.Select(template => Response(template, actor)).ToArray(), total, page, pageSize);
    }

    public async Task<TemplateResponse> GetAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, _readRoles, cancellationToken, allowBlockedOwnerRead: true);
        ImageTemplate template = await _context.Templates.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw MediaDatabase.Missing();
        if (actor.BlockReason is not null && template.OwnerId != actorId)
            throw MediaDatabase.Missing();
        if (!Moderator(actor) && !(actor.Roles.Contains("Author") && (template.OwnerId == actorId || template.Approval == "Approved" && !template.Archived && !template.Blocked)))
            throw MediaDatabase.Missing();
        return Response(template, actor);
    }

    public async Task<TemplateResponse> CreateAsync(Guid actorId, Guid sessionId, Guid imageId, string name, CancellationToken cancellationToken)
    {
        name = ValidateName(name);
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        MediaImage image = await RequireOwnImageAsync(context, actorId, imageId, cancellationToken);
        image.EverAttached = true;
        var template = new ImageTemplate { Id = Guid.NewGuid(), OwnerId = actorId, ImageId = imageId, Name = name.Trim(), CreatedAtUtc = _time.GetUtcNow() };
        context.Templates.Add(template);
        await EventAsync(context, template, actorId, "Created", null, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(template, scope.Actor);
    }

    public async Task<TemplateResponse> ActAsync(Guid actorId, Guid sessionId, Guid id, string action, long version, string? reason, Guid? imageId, string? name, CancellationToken cancellationToken)
    {
        bool authorAction = action == "submit";
        reason = reason is null ? null : MediaText.Normalize(reason, 2000, "invalid_reason");
        if (new[] { "reject", "block", "unblock" }.Contains(action) && string.IsNullOrWhiteSpace(reason))
            throw new AccountOperationException(400, "reason_required", "Provide a reason of up to 2000 characters.");
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, authorAction ? ["Author"] : _moderationRoles, cancellationToken);
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        ImageTemplate template = await LockTemplateAsync(context, id, cancellationToken);
        if (authorAction && template.OwnerId != actorId)
            throw MediaDatabase.Missing();
        if (template.Version != version)
            throw MediaDatabase.Conflict();
        await MediaDatabase.LockImageAsync(context, template.ImageId, cancellationToken);
        switch (action)
        {
            case "submit":
                if (template.Approval == "Approved" || template.Submitted)
                    throw MediaDatabase.Conflict();
                if (name is not null)
                {
                    template.Name = ValidateName(name);
                }
                MediaImage image = await RequireOwnImageAsync(context, actorId, imageId ?? template.ImageId, cancellationToken);
                image.EverAttached = true;
                template.ImageId = image.Id;
                template.Approval = "Pending";
                template.Submitted = true;
                template.Reason = null;
                break;
            case "approve":
            case "reject":
                if (template.Approval != "Pending" || !template.Submitted)
                    throw MediaDatabase.Conflict();
                template.Approval = action == "approve" ? "Approved" : "Rejected";
                template.Submitted = false;
                template.Reason = action == "reject" ? reason!.Trim() : null;
                break;
            case "archive":
            case "restore":
                if (template.Approval != "Approved" || template.Archived == (action == "archive"))
                    throw MediaDatabase.Conflict();
                template.Archived = action == "archive";
                break;
            case "block":
            case "unblock":
                if (template.Approval != "Approved" || template.Blocked == (action == "block"))
                    throw MediaDatabase.Conflict();
                template.Blocked = action == "block";
                template.Reason = reason!.Trim();
                break;
            default:
                throw new AccountOperationException(400, "invalid_action", "Unknown template action.");
        }
        template.Version++;
        await EventAsync(context, template, actorId, action, reason?.Trim(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await _notifications.FlushAfterCommitAsync();
        return Response(template, scope.Actor);
    }

    public async Task<RemovalRequestResponse> RequestRemovalAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        ImageTemplate template = await LockTemplateAsync(context, id, cancellationToken);
        if (template.OwnerId != actorId)
            throw MediaDatabase.Missing();
        if (template.Approval != "Approved")
            throw MediaDatabase.Conflict();
        TemplateRemovalRequest? request = await context.RemovalRequests.Where(item => item.TemplateId == id && (item.ResolvedAtUtc == null || template.Archived))
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            request = new TemplateRemovalRequest { Id = Guid.NewGuid(), TemplateId = id, OwnerId = actorId, CreatedAtUtc = _time.GetUtcNow(), ResolvedAtUtc = template.Archived ? _time.GetUtcNow() : null };
            context.RemovalRequests.Add(request);
            await EventAsync(context, template, actorId, "RemovalRequested", null, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        await scope.CommitAsync(cancellationToken);
        return RemovalResponse(request);
    }

    public async Task<MediaPage<RemovalRequestResponse>> RemovalQueueAsync(Guid actorId, Guid sessionId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _moderationRoles, cancellationToken);
        MediaDatabase.Page(page, pageSize);
        IQueryable<TemplateRemovalRequest> query = _context.RemovalRequests.AsNoTracking().Where(request => request.ResolvedAtUtc == null);
        int count = await query.CountAsync(cancellationToken);
        TemplateRemovalRequest[] requests = await query.OrderBy(request => request.CreatedAtUtc).ThenBy(request => request.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new MediaPage<RemovalRequestResponse>(requests.Select(RemovalResponse).ToArray(), count, page, pageSize);
    }

    public async Task ResolveRemovalAsync(Guid actorId, Guid sessionId, Guid requestId, long version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _moderationRoles, cancellationToken);
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        Guid templateId = await context.RemovalRequests.Where(item => item.Id == requestId).Select(item => item.TemplateId).SingleOrDefaultAsync(cancellationToken);
        ImageTemplate template = await LockTemplateAsync(context, templateId, cancellationToken);
        TemplateRemovalRequest request = await context.RemovalRequests.SingleAsync(item => item.Id == requestId, cancellationToken);
        if (request.ResolvedAtUtc is null)
        {
            if (request.Version != version)
                throw MediaDatabase.Conflict();
            await MediaDatabase.LockImageAsync(context, template.ImageId, cancellationToken);
            template.Archived = true;
            template.Version++;
            request.ResolvedAtUtc = _time.GetUtcNow();
            request.ResolvedBy = actorId;
            request.Version++;
            await EventAsync(context, template, actorId, "RemovalResolved", null, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        await scope.CommitAsync(cancellationToken);
        await _notifications.FlushAfterCommitAsync();
    }

    public async Task<MediaPage<TemplateHistoryResponse>> HistoryAsync(Guid actorId, Guid sessionId, Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, _readRoles, cancellationToken, allowBlockedOwnerRead: true);
        MediaDatabase.Page(page, pageSize);
        ImageTemplate template = await _context.Templates.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw MediaDatabase.Missing();
        if ((actor.BlockReason is not null || !Moderator(actor)) && !(actor.Roles.Contains("Author") && template.OwnerId == actorId))
            throw MediaDatabase.Missing();
        IQueryable<TemplateEvent> query = _context.Events.AsNoTracking().Where(entry => entry.TemplateId == id);
        int total = await query.CountAsync(cancellationToken);
        TemplateHistoryResponse[] items = await query.OrderByDescending(entry => entry.CreatedAtUtc).ThenBy(entry => entry.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(entry => new TemplateHistoryResponse(entry.Id, entry.ImageId, entry.ActorId, entry.Action, entry.Reason, entry.CreatedAtUtc, entry.Version, entry.Approval, entry.Archived, entry.Blocked, entry.Name)).ToArrayAsync(cancellationToken);
        return new MediaPage<TemplateHistoryResponse>(items, total, page, pageSize);
    }

    private async Task EventAsync(MediaDbContext context, ImageTemplate template, Guid actor, string action, string? reason, CancellationToken cancellationToken)
    {
        var entry = new TemplateEvent { Id = Guid.NewGuid(), TemplateId = template.Id, ImageId = template.ImageId, OwnerId = template.OwnerId, ActorId = actor, Action = action, Reason = reason, CreatedAtUtc = _time.GetUtcNow(), Version = template.Version, Approval = template.Approval, Archived = template.Archived, Blocked = template.Blocked, Name = template.Name };
        context.Events.Add(entry);
        await _notifications.QueueAsync(context, entry, cancellationToken);
    }

    private static async Task<MediaImage> RequireOwnImageAsync(MediaDbContext context, Guid actorId, Guid imageId, CancellationToken cancellationToken)
    {
        MediaImage image = await MediaDatabase.LockImageAsync(context, imageId, cancellationToken);
        if (image.OwnerId != actorId || image.ForSuggestion || image.ForListing)
            throw MediaDatabase.Missing();
        if (image.State != "Ready")
            throw MediaDatabase.Conflict();
        return image;
    }

    private static async Task<ImageTemplate> LockTemplateAsync(MediaDbContext context, Guid id, CancellationToken cancellationToken) =>
        (await context.Templates.FromSqlInterpolated($"SELECT * FROM media.\"Templates\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw MediaDatabase.Missing();

    private static string ValidateName(string name)
    {
        string normalized = MediaText.Normalize(name, 100, "template_name");
        if (string.IsNullOrWhiteSpace(normalized))
            throw new AccountOperationException(400, "template_name", "Provide a template name of up to 100 characters.");
        return normalized;
    }

    private static bool Moderator(AccountResponse actor) => actor.Roles.Contains("Moderator") || actor.Roles.Contains("Administrator");

    private static TemplateResponse Response(ImageTemplate item, AccountResponse actor) => new(item.Id, item.ImageId, item.Name, item.Approval, item.Submitted, item.Archived, item.Blocked, item.OwnerId == actor.Id || Moderator(actor) ? item.Reason : null, item.Version, item.OwnerId == actor.Id);

    private static RemovalRequestResponse RemovalResponse(TemplateRemovalRequest item) => new(item.Id, item.TemplateId, item.OwnerId, item.CreatedAtUtc, item.ResolvedAtUtc, item.Version);
}
