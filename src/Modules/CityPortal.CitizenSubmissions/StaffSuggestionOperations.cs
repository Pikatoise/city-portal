using System.Data.Common;
using CityPortal.Identity;
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

    internal StaffSuggestionOperations(SuggestionsDbContext context, AccountAuthorization accounts, TimeProvider time, ArticleOperations articles)
    {
        _context = context;
        _accounts = accounts;
        _time = time;
        _articles = articles;
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
                item.Version, item.ArticleId, false, false))
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
                Published = article?.HasBeenPublished == true
            };
        }).ToArray();
        return new StaffSuggestionPage(pageItems, rows.Length > _pageSize);
    }

    public async Task ChangeAssignmentAsync(Guid actorId, Guid sessionId, Guid id, int version, bool claim, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _authorRoles, cancellationToken);
        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        Suggestion? item = await context.Suggestions.FromSqlInterpolated($"SELECT * FROM citizen_submissions.\"Suggestions\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        if (item is null || item.PausedAtUtc is null && item.ExpiresAtUtc <= now
            || !claim && item.AssigneeId.HasValue && item.AssigneeId != actorId)
            throw Missing();
        if (item.Version != version || claim && item.AssigneeId.HasValue || !claim && item.AssigneeId != actorId)
            throw Conflict();
        if (claim)
        {
            item.ArticleId = await _articles.CreateFromSuggestionAsync(actorId, item.ResidentId, item.Title, item.Description,
                authorized.Connection, authorized.Transaction, cancellationToken);
            item.AssigneeId = actorId;
            item.FirstClaimedAtUtc ??= now;
            item.PausedAtUtc = now;
            item.Version++;
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (item.ArticleId is not Guid articleId || !await _articles.DeleteSuggestionDraftAsync(articleId, actorId, actorId,
            authorized.Connection, authorized.Transaction, cancellationToken))
            throw Conflict();

        await authorized.CommitAsync(cancellationToken);
        if (!claim)
            await _articles.FlushAfterCommitAsync();
    }

    public async Task DeleteAsync(Guid actorId, Guid sessionId, Guid id, DeleteSuggestionRequest request, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _moderationRoles, cancellationToken);
        string reason = request.Reason.Trim();
        if (reason.Length == 0 || reason.Contains('\0'))
            throw new AccountOperationException(400, "invalid_deletion_reason", "Enter a valid deletion reason.");

        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        IQueryable<Suggestion> target = SuggestionDatabase.Active(context, now).Where(item => item.Id == id);
        Guid? articleId = await target.Select(item => item.ArticleId).SingleOrDefaultAsync(cancellationToken);
        if (articleId is Guid linkedId)
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
