using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Identity;

public sealed class AccountAuthorization
{
    private readonly IdentityDbContext _context;
    private readonly AccountOperations _accounts;

    internal AccountAuthorization(IdentityDbContext context, AccountOperations accounts)
    {
        _context = context;
        _accounts = accounts;
    }

    public async Task<AccountResponse> RequireAsync(Guid actorId, Guid sessionId, string[] roles, CancellationToken cancellationToken, bool allowBlockedOwnerRead = false)
    {
        AccountResponse? actor = await _accounts.FindSessionAsync(sessionId, cancellationToken);
        if (actor is null || actor.Id != actorId)
            throw new AccountOperationException(401, "invalid_session", "Log in again.");
        if (!actor.EmailConfirmed || !roles.Intersect(actor.Roles, StringComparer.Ordinal).Any())
            throw new AccountOperationException(403, "media_permission_required", "A confirmed account with the required role is needed.");
        if (actor.BlockReason is not null && !allowBlockedOwnerRead)
            throw Blocked();
        return actor;
    }

    public Task<AuthorizedAccountScope> BeginAsync(Guid actorId, Guid sessionId, string[] roles, CancellationToken cancellationToken) =>
        BeginAsync(actorId, sessionId, roles, shareRoleLock: false, cancellationToken);

    /// <summary>Allows concurrent writes that serialize their own resources while excluding role changes.</summary>
    public Task<AuthorizedAccountScope> BeginSharedAsync(Guid actorId, Guid sessionId, string[] roles, CancellationToken cancellationToken) =>
        BeginAsync(actorId, sessionId, roles, shareRoleLock: true, cancellationToken);

    public async Task RequireUnblockedOwnerAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (!await _context.Users.AnyAsync(account => account.Id == ownerId && account.BlockReason == null, cancellationToken))
            throw new AccountOperationException(403, "content_owner_blocked", "The content owner's account is blocked.");
    }

    public static Task<int> LockRolesAsync(DbContext context, bool shared, CancellationToken cancellationToken) =>
        shared
            ? context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({"administration:roles"}, 0))", cancellationToken)
            : context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"administration:roles"}, 0))", cancellationToken);

    private static AccountOperationException Blocked() => new(403, "account_blocked", "The account is restricted to reading its content.");

    private async Task<AuthorizedAccountScope> BeginAsync(Guid actorId, Guid sessionId, string[] roles, bool shareRoleLock, CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Match administration's serialization order; recheck the session and roles after waiting.
            await LockRolesAsync(_context, shareRoleLock, cancellationToken);
            string? email = await _context.Users.Where(account => account.Id == actorId).Select(account => account.NormalizedEmail).SingleOrDefaultAsync(cancellationToken);
            if (email is not null)
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"email:" + email}, 0))", cancellationToken);
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"account:" + actorId}, 0))", cancellationToken);
            AccountResponse actor = await RequireAsync(actorId, sessionId, roles, cancellationToken);
            return new AuthorizedAccountScope(actor, _context.Database.GetDbConnection(), transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
