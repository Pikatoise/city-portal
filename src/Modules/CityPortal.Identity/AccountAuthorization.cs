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

    public async Task<AccountResponse> RequireAsync(Guid actorId, Guid sessionId, string[] roles, CancellationToken cancellationToken)
    {
        AccountResponse? actor = await _accounts.FindSessionAsync(sessionId, cancellationToken);
        if (actor is null || actor.Id != actorId)
            throw new AccountOperationException(401, "invalid_session", "Log in again.");
        if (!actor.EmailConfirmed || !roles.Intersect(actor.Roles, StringComparer.Ordinal).Any())
            throw new AccountOperationException(403, "media_permission_required", "A confirmed account with the required role is needed.");
        return actor;
    }

    public async Task<AuthorizedAccountScope> BeginAsync(Guid actorId, Guid sessionId, string[] roles, CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Match administration's serialization order; recheck the session and roles after waiting.
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"administration:roles"}, 0))", cancellationToken);
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
