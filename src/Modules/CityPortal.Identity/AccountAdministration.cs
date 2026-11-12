using CityPortal.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Identity;

public sealed class AccountAdministration
{
    private readonly IdentityDbContext _context;
    private readonly EmailAdministration _mail;
    private readonly TimeProvider _time;
    private static readonly string[] _roles = ["RegularUser", "Author", "Editor", "Moderator", "Administrator"];

    internal AccountAdministration(IdentityDbContext context, EmailAdministration mail, TimeProvider time)
    {
        _context = context;
        _mail = mail;
        _time = time;
    }

    public async Task BootstrapAsync(string email, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("administration:roles", cancellationToken);
        if (await AdministratorIds().AnyAsync(cancellationToken))
            throw new AccountOperationException(409, "administrator_exists", "An administrator already exists.");
        string normalized = email.Trim().ToUpperInvariant();
        Guid? id = await _context.Users.Where(account => account.NormalizedEmail == normalized).Select(account => (Guid?)account.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is null)
            throw NotFound();
        Account account = await LockAccountAsync(id.Value, cancellationToken);
        if (!account.EmailConfirmed)
            throw new AccountOperationException(403, "email_unconfirmed", "Confirm Email before administrator setup.");
        string[] roles = await RolesAsync(account.Id, cancellationToken);
        await WriteRolesAsync(account, roles, roles.Append("Administrator").Append("RegularUser").Distinct().Order(StringComparer.Ordinal).ToArray(), null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AccountPage> ListAsync(Guid actorId, Guid sessionId, string? query, string? role, bool? emailConfirmed, int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(actorId, sessionId, cancellationToken);
        ValidatePage(page, pageSize);
        string search = (query?.Trim() ?? "").ToUpperInvariant();
        if (search.Length > 254 || role is not null && !_roles.Contains(role, StringComparer.Ordinal))
            throw Validation();
        IQueryable<Account> accounts = _context.Users.AsNoTracking();
        if (search.Length > 0)
            accounts = accounts.Where(account => (account.NormalizedEmail ?? "").Contains(search) || account.DisplayName.ToUpper().Contains(search));
        if (emailConfirmed.HasValue)
            accounts = accounts.Where(account => account.EmailConfirmed == emailConfirmed.Value);
        if (role is not null)
            accounts = accounts.Where(account => _context.UserRoles.Any(assignment => assignment.UserId == account.Id && _context.Roles.Any(item => item.Id == assignment.RoleId && item.Name == role)));
        int total = await accounts.CountAsync(cancellationToken);
        AccountSummary[] items = await Summaries(accounts.OrderBy(account => account.CreatedAtUtc).ThenBy(account => account.Id).Skip((page - 1) * pageSize).Take(pageSize)).ToArrayAsync(cancellationToken);
        return new AccountPage(items, total, page, pageSize);
    }

    public async Task<AccountSummary> GetAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(actorId, sessionId, cancellationToken);
        return await Summaries(_context.Users.AsNoTracking().Where(account => account.Id == id)).SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
    }

    public async Task<RoleHistoryPage> HistoryAsync(Guid actorId, Guid sessionId, Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(actorId, sessionId, cancellationToken);
        ValidatePage(page, pageSize);
        if (!await _context.Users.AnyAsync(account => account.Id == id, cancellationToken))
            throw NotFound();
        IQueryable<RoleChange> query = _context.RoleChanges.AsNoTracking().Where(change => change.TargetAccountId == id);
        int total = await query.CountAsync(cancellationToken);
        RoleHistoryItem[] items = await query.OrderByDescending(change => change.CreatedAtUtc).ThenBy(change => change.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(change => new RoleHistoryItem(change.Id, change.ActorId, change.ActorKind, change.TargetAccountId, change.CreatedAtUtc, change.OldRoles, change.NewRoles)).ToArrayAsync(cancellationToken);
        return new RoleHistoryPage(items, total, page, pageSize);
    }

    public async Task<AccountSummary> ChangeRolesAsync(Guid actorId, Guid sessionId, Guid targetId, string[]? roles, long version, CancellationToken cancellationToken)
    {
        if (roles is null || roles.Any(role => !_roles.Contains(role, StringComparer.Ordinal)))
            throw Validation();
        string[] desired = roles.Append("RegularUser").Distinct().Order(StringComparer.Ordinal).ToArray();
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("administration:roles", cancellationToken);
        await LockAccountAsync(actorId, cancellationToken);
        await RequireAdministratorAsync(actorId, sessionId, cancellationToken);
        Account target = await LockAccountAsync(targetId, cancellationToken);
        if (target.Version != version)
            throw new AccountOperationException(409, "version_conflict", "The account changed. Reload it.");
        string[] old = await RolesAsync(targetId, cancellationToken);
        if (old.Contains("Administrator") && !desired.Contains("Administrator") && await AdministratorIds().CountAsync(cancellationToken) == 1)
            throw new AccountOperationException(409, "last_administrator", "The last administrator cannot lose the role.");
        if (!old.SequenceEqual(desired))
            await WriteRolesAsync(target, old, desired, actorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Self-removal is authorized by the pre-write check; do not require the removed role to project the result.
        return new AccountSummary(target.Id, target.Email ?? "", target.DisplayName, target.EmailConfirmed, desired, target.Version);
    }

    public async Task<EmailDeliveryPage> DeliveriesAsync(Guid actorId, Guid sessionId, string? state, int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(actorId, sessionId, cancellationToken);
        ValidatePage(page, pageSize);
        if (state is not null && !EmailAdministration.States.Contains(state))
            throw Validation();
        return await _mail.ListAsync(state, page, pageSize, cancellationToken);
    }

    public async Task ReplayAsync(Guid actorId, Guid sessionId, Guid id, long version, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("administration:roles", cancellationToken);
        await LockAccountAsync(actorId, cancellationToken);
        await RequireAdministratorAsync(actorId, sessionId, cancellationToken);
        EmailReplayResult result = await _mail.ReplayAsync(id, version, actorId, _context.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
        if (result == EmailReplayResult.NotFound)
            throw NotFound();
        if (result == EmailReplayResult.InvalidLink)
            throw new AccountOperationException(400, "invalid_link", "The delivery link is no longer valid.");
        if (result == EmailReplayResult.Conflict)
            throw new AccountOperationException(409, "version_conflict", "Only current failed deliveries can be replayed.");
        await transaction.CommitAsync(cancellationToken);
        await _mail.FlushAfterCommitAsync();
    }

    private async Task RequireAdministratorAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (!await _context.Sessions.AnyAsync(session => session.Id == sessionId && session.AccountId == actorId && !session.Revoked && session.ExpiresAtUtc > now, cancellationToken))
            throw new AccountOperationException(401, "invalid_session", "Log in again.");
        if (!await _context.Users.AnyAsync(account => account.Id == actorId && account.EmailConfirmed, cancellationToken)
            || !await AdministratorIds().AnyAsync(id => id == actorId, cancellationToken))
            throw new AccountOperationException(403, "administrator_required", "A confirmed administrator is required.");
    }

    private IQueryable<Guid> AdministratorIds() => from assignment in _context.UserRoles
                                                   join role in _context.Roles on assignment.RoleId equals role.Id
                                                   where role.Name == "Administrator"
                                                   select assignment.UserId;

    private Task<string[]> RolesAsync(Guid id, CancellationToken cancellationToken) =>
        (from assignment in _context.UserRoles
         join role in _context.Roles on assignment.RoleId equals role.Id
         where assignment.UserId == id
         orderby role.Name
         select role.Name ?? "").ToArrayAsync(cancellationToken);

    private IQueryable<AccountSummary> Summaries(IQueryable<Account> query) => query.Select(account => new AccountSummary(account.Id, account.Email ?? "", account.DisplayName, account.EmailConfirmed,
        (from assignment in _context.UserRoles join role in _context.Roles on assignment.RoleId equals role.Id where assignment.UserId == account.Id orderby role.Name select role.Name ?? "").ToArray(), account.Version));

    private async Task WriteRolesAsync(Account account, string[] old, string[] desired, Guid? actorId, CancellationToken cancellationToken)
    {
        await _context.UserRoles.Where(assignment => assignment.UserId == account.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (string name in desired)
        {
            IdentityRole<Guid>? role = await _context.Roles.SingleOrDefaultAsync(item => item.Name == name, cancellationToken);
            if (role is null)
            {
                role = new IdentityRole<Guid>(name) { Id = Guid.NewGuid(), NormalizedName = name.ToUpperInvariant() };
                _context.Roles.Add(role);
            }
            _context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = account.Id, RoleId = role.Id });
        }
        account.Version++;
        _context.RoleChanges.Add(new RoleChange { Id = Guid.NewGuid(), ActorId = actorId, ActorKind = actorId.HasValue ? "Account" : "Bootstrap", TargetAccountId = account.Id, CreatedAtUtc = _time.GetUtcNow(), OldRoles = old, NewRoles = desired });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Account> LockAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        string? email = await _context.Users.Where(account => account.Id == id).Select(account => account.NormalizedEmail).SingleOrDefaultAsync(cancellationToken);
        if (email is null)
            throw NotFound();
        await LockAsync("email:" + email, cancellationToken);
        await LockAsync("account:" + id, cancellationToken);
        return await _context.Users.SingleAsync(account => account.Id == id, cancellationToken);
    }

    private Task<int> LockAsync(string key, CancellationToken cancellationToken) => _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw Validation();
    }

    private static AccountOperationException Validation() => new(400, "validation_failed", "Invalid filter, roles, or pagination.");
    private static AccountOperationException NotFound() => new(404, "not_found", "The requested account or delivery is unavailable.");
}
