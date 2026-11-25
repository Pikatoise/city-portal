using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Identity;

public sealed class AccountBlocking
{
    private const int _pageSize = 20;
    private static readonly string[] _roles = ["Moderator", "Administrator"];
    private readonly IdentityDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;
    private readonly IEnumerable<IAccountBlockObserver> _observers;
    private readonly AccountOperations _accounts;
    private readonly WorkflowNotificationQueue _notifications;

    internal AccountBlocking(IdentityDbContext context, AccountAuthorization authorization, TimeProvider time, IEnumerable<IAccountBlockObserver> observers,
        AccountOperations accounts, WorkflowNotificationQueue notifications)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
        _observers = observers;
        _accounts = accounts;
        _notifications = notifications;
    }

    public async Task<AccountBlockPage> ListAsync(Guid actorId, Guid sessionId, string? query, int page, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        string search = query?.Trim().ToUpperInvariant() ?? "";
        if (page is < 1 or > 100000 || search.Length > 254)
            throw new AccountOperationException(400, "invalid_account_search", "Choose a valid search and page number.");

        IQueryable<Account> accounts = _context.Users.AsNoTracking().Where(account => account.Id != actorId);
        if (!actor.Roles.Contains("Administrator"))
            accounts = accounts.Where(account => !StaffIds().Contains(account.Id));
        if (search.Length > 0)
            accounts = accounts.Where(account => (account.NormalizedEmail ?? "").Contains(search) || account.DisplayName.ToUpper().Contains(search));

        AccountBlockSummary[] rows = await accounts.OrderBy(account => account.CreatedAtUtc).ThenBy(account => account.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(account => new AccountBlockSummary(account.Id, account.Email ?? "", account.DisplayName, account.Version, account.BlockReason))
            .ToArrayAsync(cancellationToken);
        return new AccountBlockPage(rows.Take(_pageSize).ToArray(), rows.Length > _pageSize);
    }

    public async Task<AccountBlockHistory> ReadOwnHistoryAsync(Guid sessionId, int page, CancellationToken cancellationToken)
    {
        AccountResponse? account = await _accounts.FindSessionAsync(sessionId, cancellationToken);
        if (account is null)
            throw new AccountOperationException(401, "invalid_session", "Log in again.");

        return await ReadHistoryAsync(account.Id, page, includeActor: false, cancellationToken);
    }

    public async Task<AccountBlockHistory> ReadStaffHistoryAsync(Guid actorId, Guid sessionId, Guid targetId, int page, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(actorId, sessionId, _roles, cancellationToken);
        IQueryable<Account> accounts = _context.Users.AsNoTracking();
        if (!scope.Actor.Roles.Contains("Administrator"))
            accounts = accounts.Where(account => !StaffIds().Contains(account.Id));
        if (!await accounts.AnyAsync(account => account.Id == targetId, cancellationToken))
            throw new AccountOperationException(404, "account_not_found", "The account is unavailable.");

        return await ReadHistoryAsync(targetId, page, includeActor: true, cancellationToken);
    }

    private async Task<AccountBlockHistory> ReadHistoryAsync(Guid accountId, int page, bool includeActor, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        AccountBlockHistoryItem[] rows = await _context.BlockDecisions.AsNoTracking().Where(item => item.TargetId == accountId)
            .OrderByDescending(item => item.DecidedAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new AccountBlockHistoryItem(item.Id, item.Reason != null, item.DecidedAtUtc, item.Reason,
                includeActor ? item.ActorId : null, includeActor ? _context.Users.Where(user => user.Id == item.ActorId)
                    .Select(user => user.DisplayName).FirstOrDefault() : null))
            .ToArrayAsync(cancellationToken);
        return new AccountBlockHistory(rows.Take(_pageSize).ToArray(), rows.Length > _pageSize);
    }

    public async Task ChangeAsync(Guid actorId, Guid sessionId, Guid accountId, long version, string? reason, bool block, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        if (actorId == accountId)
            throw Forbidden();
        if (!scope.Actor.Roles.Contains("Administrator") && await StaffIds().AnyAsync(id => id == accountId, cancellationToken))
            throw Forbidden();

        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"account:" + accountId}, 0))", cancellationToken);
        Account target = await _context.Users.SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken)
            ?? throw new AccountOperationException(404, "account_not_found", "The account is unavailable.");
        if (target.Version != version || block == (target.BlockReason is not null))
            throw new AccountOperationException(409, "version_conflict", "The account changed. Reload it.");

        string? normalizedReason = reason?.Trim();
        if (block && (normalizedReason is null || normalizedReason.Length is < 1 or > 500 || normalizedReason.Contains('\0')))
            throw new AccountOperationException(400, "invalid_block_reason", "Enter a reason of up to 500 characters.");

        IQueryable<Guid> administrators = AdministratorIds();
        if (block && target.EmailConfirmed && await administrators.AnyAsync(id => id == accountId, cancellationToken)
            && !await _context.Users.AnyAsync(account => account.Id != accountId && account.EmailConfirmed
                && account.BlockReason == null && administrators.Contains(account.Id), cancellationToken))
            throw new AccountOperationException(409, "last_administrator", "The last active administrator cannot be blocked.");

        target.BlockReason = block ? normalizedReason : null;
        target.Version++;
        var decision = new AccountBlockDecision
        {
            Id = Guid.NewGuid(),
            TargetId = accountId,
            ActorId = actorId,
            DecidedAtUtc = _time.GetUtcNow(),
            Reason = target.BlockReason
        };
        _context.BlockDecisions.Add(decision);
        await _context.SaveChangesAsync(cancellationToken);
        if (block)
            foreach (IAccountBlockObserver observer in _observers)
                await observer.HideForAccountBlockAsync(accountId, scope, cancellationToken);
        await _notifications.QueueAsync(decision.Id, block ? "account.blocked" : "account.unblocked", accountId,
            decision.DecidedAtUtc, [new WorkflowRecipient(accountId, target.EmailConfirmed ? target.Email : null)],
            scope.Connection, scope.Transaction, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await _notifications.FlushAfterCommitAsync();
    }

    private IQueryable<Guid> StaffIds() =>
        from assignment in _context.UserRoles
        join role in _context.Roles on assignment.RoleId equals role.Id
        where role.Name != "RegularUser"
        select assignment.UserId;

    private IQueryable<Guid> AdministratorIds() =>
        from assignment in _context.UserRoles
        join role in _context.Roles on assignment.RoleId equals role.Id
        where role.Name == "Administrator"
        select assignment.UserId;

    private static AccountOperationException Forbidden() => new(403, "account_block_forbidden", "You cannot change this account's block.");
}
