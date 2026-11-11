using System.Security.Cryptography;
using System.Text;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Identity;

public sealed class AccountOperations
{
    private readonly IdentityDbContext _context;
    private readonly AccountMailQueue _mail;
    private readonly TimeProvider _time;
    private readonly PasswordHasher<Account> _passwords = new();

    internal AccountOperations(IdentityDbContext context, AccountMailQueue mail, TimeProvider time)
    {
        _context = context;
        _mail = mail;
        _time = time;
    }

    public async Task EnforceIpLimitAsync(string ip, string purpose, int limit, TimeSpan window, CancellationToken cancellationToken)
    {
        string key = purpose + ":" + Hash(ip);
        DateTimeOffset now = _time.GetUtcNow();
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(key, cancellationToken);
        DateTimeOffset[] attempts = await _context.RequestAttempts.Where(attempt => attempt.Key == key && attempt.CreatedAtUtc > now - window)
            .OrderBy(attempt => attempt.CreatedAtUtc).Select(attempt => attempt.CreatedAtUtc).ToArrayAsync(cancellationToken);
        _context.RequestAttempts.Add(new RequestAttempt { Key = key, CreatedAtUtc = now });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (attempts.Length >= limit)
            throw RateLimit((int)Math.Ceiling((attempts[attempts.Length - limit] + window - now).TotalSeconds));
    }

    public async Task<AccountResponse> RegisterAsync(string? email, string? displayName, string? password, CancellationToken cancellationToken)
    {
        (string cleanEmail, string cleanName) = AccountValidation.ValidateRegistration(email, displayName, password);
        string normalized = cleanEmail.ToUpperInvariant();
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("email:" + normalized, cancellationToken);
        if (await _context.Users.AnyAsync(account => account.NormalizedEmail == normalized, cancellationToken))
            throw new AccountOperationException(409, "duplicate_email", "This Email is registered. Log in or recover your password.");
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = cleanEmail,
            NormalizedEmail = normalized,
            UserName = cleanEmail,
            NormalizedUserName = normalized,
            DisplayName = cleanName,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAtUtc = _time.GetUtcNow(),
            LockoutEnabled = true
        };
        account.PasswordHash = _passwords.HashPassword(account, password!);
        _context.Users.Add(account);
        await LockAsync("role:RegularUser", cancellationToken);
        IdentityRole<Guid>? role = await _context.Roles.SingleOrDefaultAsync(item => item.Name == "RegularUser", cancellationToken);
        if (role is null)
        {
            role = new IdentityRole<Guid>("RegularUser") { Id = Guid.NewGuid(), NormalizedName = "REGULARUSER" };
            _context.Roles.Add(role);
        }
        _context.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid> { UserId = account.Id, RoleId = role.Id });
        await QueueConfirmationAsync(account, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _mail.FlushAfterCommitAsync();
        return await ProjectAsync(account, cancellationToken);
    }

    public async Task<(AccountResponse Account, Guid SessionId, DateTimeOffset ExpiresAtUtc)> LoginAsync(string? email, string? password, bool rememberMe, CancellationToken cancellationToken)
    {
        string normalized = (email?.Trim() ?? string.Empty).ToUpperInvariant();
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("email:" + normalized, cancellationToken);
        Account? account = await _context.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalized, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        if (account?.LockoutEnd > now)
            throw RateLimit((int)Math.Ceiling((account.LockoutEnd.Value - now).TotalSeconds));
        if (account is null || string.IsNullOrEmpty(password) || _passwords.VerifyHashedPassword(account, account.PasswordHash ?? string.Empty, password) == PasswordVerificationResult.Failed)
        {
            if (account is not null)
            {
                account.AccessFailedCount++;
                if (account.AccessFailedCount >= 5)
                {
                    account.LockoutEnd = now.AddMinutes(15);
                    account.AccessFailedCount = 0;
                }
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (account.LockoutEnd > now)
                    throw RateLimit(900);
            }
            throw new AccountOperationException(401, "invalid_login", "Email or password is incorrect.");
        }
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
        var session = new AccountSession { Id = Guid.NewGuid(), AccountId = account.Id, ExpiresAtUtc = rememberMe ? now.AddDays(30) : now.AddHours(12) };
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await ProjectAsync(account, cancellationToken), session.Id, session.ExpiresAtUtc);
    }

    public async Task<AccountResponse?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        Guid? accountId = await _context.Sessions.Where(session => session.Id == sessionId && !session.Revoked && session.ExpiresAtUtc > now)
            .Select(session => (Guid?)session.AccountId).SingleOrDefaultAsync(cancellationToken);
        Account? account = accountId.HasValue ? await _context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == accountId.Value, cancellationToken) : null;
        return account is null ? null : await ProjectAsync(account, cancellationToken);
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await _context.Sessions.Where(session => session.Id == sessionId).ExecuteUpdateAsync(update => update.SetProperty(session => session.Revoked, true), cancellationToken);
    }

    public async Task<ConfirmationDeliveryResponse> ResendAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync("account:" + accountId, cancellationToken);
        Account account = await _context.Users.SingleAsync(item => item.Id == accountId, cancellationToken);
        if (account.EmailConfirmed)
            throw new AccountOperationException(409, "already_confirmed", "Email is already confirmed.");
        int wait = await ResendWaitAsync(accountId, cancellationToken);
        if (wait > 0)
            throw RateLimit(wait);
        await _context.Links.Where(link => link.AccountId == accountId && link.State == "Active")
            .ExecuteUpdateAsync(update => update.SetProperty(link => link.State, "Invalidated"), cancellationToken);
        await QueueConfirmationAsync(account, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _mail.FlushAfterCommitAsync();
        return await DeliveryAsync(account, cancellationToken);
    }

    public async Task<string> ConfirmAsync(Guid linkId, string? token, CancellationToken cancellationToken)
    {
        if (linkId == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length > 128)
            throw InvalidLink();
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        AccountLink? initial = await _context.Links.AsNoTracking().SingleOrDefaultAsync(link => link.Id == linkId, cancellationToken);
        if (initial is null)
            throw InvalidLink();
        await LockAsync("account:" + initial.AccountId, cancellationToken);
        AccountLink link = await _context.Links.SingleAsync(item => item.Id == linkId, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(link.TokenHash), SHA256.HashData(Encoding.UTF8.GetBytes(token))) || link.State == "Invalidated")
            throw InvalidLink();
        Account account = await _context.Users.SingleAsync(item => item.Id == link.AccountId, cancellationToken);
        if (link.State == "Consumed" && account.EmailConfirmed)
            return "AlreadyConfirmed";
        if (link.ExpiresAtUtc <= _time.GetUtcNow())
            throw new AccountOperationException(400, "expired_link", "The confirmation link has expired.");
        link.State = "Consumed";
        account.EmailConfirmed = true;
        account.Version++;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return "Confirmed";
    }

    private async Task QueueConfirmationAsync(Account account, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var link = new AccountLink { Id = Guid.NewGuid(), AccountId = account.Id, TokenHash = Hash(token), CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(24) };
        var messageId = Guid.NewGuid();
        _context.Links.Add(link);
        account.LatestConfirmationMessageId = messageId;
        await _context.SaveChangesAsync(cancellationToken);
        await _mail.QueueAsync(new AccountMailRequest
        {
            MessageId = messageId,
            AccountId = account.Id,
            LinkId = link.Id,
            Recipient = account.Email!,
            Token = token,
            Purpose = "confirmation",
            CreatedAtUtc = now,
            ExpiresAtUtc = link.ExpiresAtUtc
        }, _context.Database.GetDbConnection(), _context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
    }

    private async Task<AccountResponse> ProjectAsync(Account account, CancellationToken cancellationToken)
    {
        string[] roles = await (from assignment in _context.Set<IdentityUserRole<Guid>>()
                                join role in _context.Roles on assignment.RoleId equals role.Id
                                where assignment.UserId == account.Id
                                orderby role.Name
                                select role.Name!).ToArrayAsync(cancellationToken);
        return new AccountResponse
        {
            Id = account.Id,
            Email = account.Email!,
            DisplayName = account.DisplayName,
            EmailConfirmed = account.EmailConfirmed,
            Roles = roles,
            Version = account.Version,
            ConfirmationDelivery = await DeliveryAsync(account, cancellationToken)
        };
    }

    private async Task<ConfirmationDeliveryResponse> DeliveryAsync(Account account, CancellationToken cancellationToken)
    {
        string state = "Loading";
        if (account.LatestConfirmationMessageId is Guid messageId)
        {
            AccountMailStatus delivery = await _mail.GetLatestAsync(messageId, cancellationToken);
            if (delivery is not null)
                state = delivery.State == "Pending" ? "Loading" : delivery.State;
        }
        return new ConfirmationDeliveryResponse { State = state, ResendAfterSeconds = await ResendWaitAsync(account.Id, cancellationToken) };
    }

    private async Task<int> ResendWaitAsync(Guid accountId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset[] links = await _context.Links.Where(link => link.AccountId == accountId && link.CreatedAtUtc > now.AddHours(-1))
            .OrderByDescending(link => link.CreatedAtUtc).Select(link => link.CreatedAtUtc).ToArrayAsync(cancellationToken);
        double wait = links.Length == 0 ? 0 : (links[0].AddSeconds(60) - now).TotalSeconds;
        if (links.Length >= 5)
            wait = Math.Max(wait, (links[4].AddHours(1) - now).TotalSeconds);
        return Math.Max(0, (int)Math.Ceiling(wait));
    }

    private async Task LockAsync(string key, CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AccountOperationException RateLimit(int wait) => new(429, "rate_limited", "Please wait before trying again.", retryAfterSeconds: Math.Max(1, wait));
    private static AccountOperationException InvalidLink() => new(400, "invalid_link", "The confirmation link is invalid.");
}
