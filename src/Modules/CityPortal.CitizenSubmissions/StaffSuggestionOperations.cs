using System.Data.Common;
using CityPortal.Identity;
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

    internal StaffSuggestionOperations(SuggestionsDbContext context, AccountAuthorization accounts, TimeProvider time)
    {
        _context = context;
        _accounts = accounts;
        _time = time;
    }

    public async Task<StaffSuggestionPage> ListAsync(Guid actorId, Guid sessionId, bool moderation, bool mine, int page, CancellationToken cancellationToken)
    {
        await _accounts.RequireAsync(actorId, sessionId, moderation ? _moderationRoles : _authorRoles, cancellationToken);
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        IQueryable<Suggestion> query = Active(_context, _time.GetUtcNow());
        if (!moderation)
            query = mine ? query.Where(item => item.AssigneeId == actorId) : query.Where(item => item.AssigneeId == null);
        bool includeContact = !moderation && mine;
        StaffSuggestionResponse[] rows = await query.AsNoTracking().OrderBy(item => item.ExpiresAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new StaffSuggestionResponse(item.Id, item.Title, item.Description, item.EventDate, item.Location,
                includeContact ? item.Contact : null, item.AssigneeId != null, item.ExpiresAtUtc, item.Version))
            .ToArrayAsync(cancellationToken);
        return new StaffSuggestionPage(rows.Take(_pageSize).ToArray(), rows.Length > _pageSize);
    }

    public async Task ChangeAssignmentAsync(Guid actorId, Guid sessionId, Guid id, int version, bool claim, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _authorRoles, cancellationToken);
        await using SuggestionsDbContext context = await JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        IQueryable<Suggestion> target = Active(context, now).Where(item => item.Id == id);
        int changed;
        if (claim)
            changed = await target.Where(item => item.Version == version && item.AssigneeId == null)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.AssigneeId, (Guid?)actorId)
                    .SetProperty(item => item.FirstClaimedAtUtc, item => item.FirstClaimedAtUtc ?? now)
                    .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        else
            changed = await target.Where(item => item.Version == version && item.AssigneeId == actorId)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.AssigneeId, (Guid?)null)
                    .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);

        if (changed == 0)
        {
            if (!await target.AnyAsync(item => claim || item.AssigneeId == actorId || item.AssigneeId == null, cancellationToken))
                throw Missing();
            throw Conflict();
        }
        await authorized.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid actorId, Guid sessionId, Guid id, DeleteSuggestionRequest request, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope authorized = await _accounts.BeginAsync(actorId, sessionId, _moderationRoles, cancellationToken);
        string reason = request.Reason.Trim();
        if (reason.Length == 0 || reason.Contains('\0'))
            throw new AccountOperationException(400, "invalid_deletion_reason", "Enter a valid deletion reason.");

        await using SuggestionsDbContext context = await JoinAsync(authorized.Connection, authorized.Transaction, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        IQueryable<Suggestion> target = Active(context, now).Where(item => item.Id == id);
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

        await using SuggestionsDbContext context = await JoinAsync(connection, transaction, cancellationToken);
        await Active(context, _time.GetUtcNow()).Where(item => item.AssigneeId == accountId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.AssigneeId, (Guid?)null)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
    }

    Task IAccountRoleChangeObserver.FlushAfterCommitAsync() => Task.CompletedTask;

    private static IQueryable<Suggestion> Active(SuggestionsDbContext context, DateTimeOffset now) =>
        context.Suggestions.Where(item => item.ExpiresAtUtc > now);

    private static async Task<SuggestionsDbContext> JoinAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var context = new SuggestionsDbContext(new DbContextOptionsBuilder<SuggestionsDbContext>().UseNpgsql(connection).Options);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return context;
    }

    private static AccountOperationException Missing() => new(404, "suggestion_not_found", "The suggestion is unavailable.");

    private static AccountOperationException Conflict() => new(409, "suggestion_conflict", "The suggestion changed. Reload the queue.");
}
