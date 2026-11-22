using System.Data.Common;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.CitizenSubmissions;

public sealed class StaffSuggestionOperations : IAccountRoleChangeObserver
{
    private const int _pageSize = 20;
    private static readonly string[] _authorRoles = ["Author"];
    private static readonly string[] _moderationRoles = ["Moderator", "Administrator"];
    private readonly SuggestionsDbContext _context;
    private readonly AccountAuthorization _accounts;
    private readonly TimeProvider _time;
    private readonly ArticleOperations _articles;
    private readonly SuggestionImages _images;

    internal StaffSuggestionOperations(SuggestionsDbContext context, AccountAuthorization accounts, TimeProvider time, ArticleOperations articles,
        SuggestionImages images)
    {
        _context = context;
        _accounts = accounts;
        _time = time;
        _articles = articles;
        _images = images;
    }

    public async Task<StaffSuggestionPage> ListAsync(Guid actorId, Guid sessionId, bool moderation, bool mine, int page, CancellationToken cancellationToken)
    {
        await _accounts.RequireAsync(actorId, sessionId, moderation ? _moderationRoles : _authorRoles, cancellationToken);
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        IQueryable<Suggestion> query = SuggestionDatabase.Active(_context, _time.GetUtcNow());
        if (!moderation)
            query = mine ? query.Where(item => item.AssigneeId == actorId) : query.Where(item => item.AssigneeId == null);
        bool includeContact = !moderation && mine;
        StaffSuggestionResponse[] rows = await query.AsNoTracking().OrderBy(item => item.ExpiresAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new StaffSuggestionResponse(item.Id, item.Title, item.Description, item.EventDate, item.Location,
                includeContact ? item.Contact : null, item.AssigneeId != null, item.PausedAtUtc == null ? item.ExpiresAtUtc : null,
                item.Version, item.ArticleId, false, false, false))
            .ToArrayAsync(cancellationToken);
        StaffSuggestionResponse[] pageItems = rows.Take(_pageSize).ToArray();
        Guid[] ids = pageItems.Where(item => item.ArticleId.HasValue).Select(item => item.ArticleId.GetValueOrDefault()).ToArray();
        Dictionary<Guid, SuggestionArticleStatus> articles = await _articles.FindSuggestionArticlesAsync(ids, cancellationToken);
        pageItems = pageItems.Select(item =>
        {
            SuggestionArticleStatus? article = articles.GetValueOrDefault(item.ArticleId.GetValueOrDefault());
            return item with
            {
                ArticleId = includeContact ? item.ArticleId : null,
                CanReturn = includeContact && article?.HasBeenPublished == false,
                Published = article?.HasBeenPublished == true,
                CanImportImages = includeContact && article?.CanImportImages == true
            };
        }).ToArray();
        return new StaffSuggestionPage(pageItems, rows.Length > _pageSize);
    }

    public async Task<ImageResponse[]> ImagesAsync(Guid actorId, Guid sessionId, Guid suggestionId, CancellationToken cancellationToken)
    {
        await RequireImageAccessAsync(actorId, sessionId, suggestionId, cancellationToken);
        return await _images.ReadAsync(suggestionId, cancellationToken);
    }

    public async Task<Stream> OpenImageAsync(Guid actorId, Guid sessionId, Guid suggestionId, Guid imageId, CancellationToken cancellationToken)
    {
        await RequireImageAccessAsync(actorId, sessionId, suggestionId, cancellationToken);
        return await _images.OpenAsync(suggestionId, imageId, cancellationToken);
    }

    public async Task ImportImagesAsync(Guid actorId, Guid sessionId, Guid suggestionId, Guid[] ids, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _authorRoles, cancellationToken);
        Guid? article = await SuggestionDatabase.Active(_context, _time.GetUtcNow())
            .Where(item => item.Id == suggestionId && item.AssigneeId == actorId).Select(item => item.ArticleId).SingleOrDefaultAsync(cancellationToken);
        if (article is not Guid articleId)
            throw Missing();
        if (ids.Length is < 1 or > 5 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new AccountOperationException(400, "invalid_suggestion_images", "Select one to five distinct images.");
        await _images.RequireReadyAsync(suggestionId, ids, cancellationToken);
        await _articles.ImportSuggestionImagesAsync(articleId, actorId, suggestionId, ids, authorized.Connection, authorized.Transaction, cancellationToken);
        await authorized.CommitAsync(cancellationToken);
    }

    private async Task RequireImageAccessAsync(Guid actorId, Guid sessionId, Guid suggestionId, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _accounts.RequireAsync(actorId, sessionId, ["Author", "Moderator", "Administrator"], cancellationToken);
        bool moderator = actor.Roles.Any(_moderationRoles.Contains);
        if (!await SuggestionDatabase.Active(_context, _time.GetUtcNow())
            .AnyAsync(item => item.Id == suggestionId && (moderator || item.AssigneeId == null || item.AssigneeId == actorId), cancellationToken))
            throw Missing();
    }

    public Task ChangeAssignmentAsync(Guid actorId, Guid sessionId, Guid id, int version, bool claim, CancellationToken cancellationToken) =>
        claim ? ClaimAsync(actorId, sessionId, id, version, cancellationToken) : ReturnAsync(actorId, sessionId, id, version, cancellationToken);

    private async Task ClaimAsync(Guid actorId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _authorRoles, cancellationToken);
        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        Suggestion? item = await LockSuggestionAsync(context, id, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        if (item is null || item.IsExpiredAt(now))
            throw Missing();
        if (item.Version != version || item.AssigneeId.HasValue)
            throw Conflict();

        Guid articleId = await _articles.CreateFromSuggestionAsync(actorId, item.ResidentId, item.Title, item.Description,
            authorized.Connection, authorized.Transaction, cancellationToken);
        item.Claim(actorId, articleId, now);
        await context.SaveChangesAsync(cancellationToken);
        await authorized.CommitAsync(cancellationToken);
    }

    private async Task ReturnAsync(Guid actorId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _authorRoles, cancellationToken);
        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        Suggestion? item = await LockSuggestionAsync(context, id, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        if (item is null || item.IsExpiredAt(now) || item.AssigneeId.HasValue && item.AssigneeId != actorId)
            throw Missing();
        if (item.Version != version || item.AssigneeId != actorId)
            throw Conflict();

        // News notifies the deletion observer in this transaction to return the suggestion.
        if (item.ArticleId is not Guid articleId || !await _articles.DeleteSuggestionDraftAsync(articleId, actorId, actorId,
            authorized.Connection, authorized.Transaction, cancellationToken))
            throw Conflict();

        await authorized.CommitAsync(cancellationToken);
        await _articles.FlushAfterCommitAsync();
    }

    private static Task<Suggestion?> LockSuggestionAsync(SuggestionsDbContext context, Guid id, CancellationToken cancellationToken) =>
        context.Suggestions.FromSqlInterpolated($"SELECT * FROM citizen_submissions.\"Suggestions\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task DeleteAsync(Guid actorId, Guid sessionId, Guid id, DeleteSuggestionRequest request, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _moderationRoles, cancellationToken);
        string reason = request.Reason.Trim();
        if (reason.Length == 0 || reason.Contains('\0'))
            throw new AccountOperationException(400, "invalid_deletion_reason", "Enter a valid deletion reason.");

        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        IQueryable<Suggestion> target = SuggestionDatabase.Active(context, now).Where(item => item.Id == id);
        Suggestion? item = await target.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (item is null)
            throw Missing();
        if (item.ArticleId is Guid linkedId)
        {
            Dictionary<Guid, SuggestionArticleStatus> articles = await _articles.FindSuggestionArticlesAsync([linkedId], cancellationToken);
            if (articles.GetValueOrDefault(linkedId)?.HasBeenPublished == true)
                throw new AccountOperationException(409, "suggestion_published", "A published suggestion is retained in the resident's account.");
        }
        int deleted = await target.Where(item => item.Version == request.Version).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            if (!await target.AnyAsync(cancellationToken))
                throw Missing();
            throw Conflict();
        }
        await _images.ReplaceAsync(id, item.ResidentId, [], authorized.Connection, authorized.Transaction, cancellationToken);
        context.Deletions.Add(new SuggestionDeletion { SuggestionId = id, ActorId = actorId, DeletedAtUtc = now, Reason = reason });
        await context.SaveChangesAsync(cancellationToken);
        await authorized.CommitAsync(cancellationToken);
    }

    async Task IAccountRoleChangeObserver.RolesChangedAsync(Guid actorId, Guid accountId, string[] oldRoles, string[] newRoles,
        DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (!oldRoles.Contains("Author") || newRoles.Contains("Author"))
            return;

        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(connection, transaction, cancellationToken);
        Guid[] drafts = await context.Suggestions.Where(item => item.AssigneeId == accountId && item.ArticleId != null)
            .OrderBy(item => item.ArticleId).Select(item => item.ArticleId.GetValueOrDefault()).ToArrayAsync(cancellationToken);
        foreach (Guid draft in drafts)
            await _articles.DeleteSuggestionDraftAsync(draft, accountId, actorId, connection, transaction, cancellationToken);
    }

    Task IAccountRoleChangeObserver.FlushAfterCommitAsync() => Task.CompletedTask;

    private static AccountOperationException Missing() => new(404, "suggestion_not_found", "The suggestion is unavailable.");

    private static AccountOperationException Conflict() => new(409, "suggestion_conflict", "The suggestion changed. Reload the queue.");
}
