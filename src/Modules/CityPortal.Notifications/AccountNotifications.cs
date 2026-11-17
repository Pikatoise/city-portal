using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Notifications;

public sealed class AccountNotifications
{
    private readonly NotificationsDbContext _context;
    private readonly TimeProvider _time;

    internal AccountNotifications(NotificationsDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async Task<AccountNotificationPage> ListAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = _time.GetUtcNow().AddDays(-30);
        IQueryable<AccountNotification> query = _context.AccountNotifications.AsNoTracking().Where(item => item.AccountId == accountId && item.CreatedAtUtc > cutoff);
        int total = await query.CountAsync(cancellationToken);
        AccountNotification[] items = await query.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new AccountNotificationPage(items.Select(item => new AccountNotificationResponse(item.Id, item.Type, WorkflowNotificationTypes.Message(item.Type), item.ResourceId, item.CreatedAtUtc, item.Read)).ToArray(), total, page, pageSize);
    }

    public async Task<bool> MarkReadAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = _time.GetUtcNow().AddDays(-30);
        return await _context.AccountNotifications.Where(item => item.AccountId == accountId && item.Id == id && item.CreatedAtUtc > cutoff)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Read, true), cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken cancellationToken) =>
        await _context.AccountNotifications.Where(item => item.AccountId == accountId && item.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<NotificationPreferenceResponse[]> PreferencesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        NotificationPreference[] saved = await _context.Preferences.AsNoTracking().Where(item => item.AccountId == accountId).ToArrayAsync(cancellationToken);
        return WorkflowNotificationTypes.All.Select(type => saved.SingleOrDefault(item => item.Type == type) is { } item
            ? new NotificationPreferenceResponse(type, item.Account, item.Email) : new NotificationPreferenceResponse(type, true, true)).ToArray();
    }

    public async Task SavePreferencesAsync(Guid accountId, NotificationPreferenceResponse[] preferences, CancellationToken cancellationToken)
    {
        // Serialize writes from two tabs without overwriting switches omitted by either caller.
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"notification-preferences:" + accountId}, 0))", cancellationToken);
        foreach (NotificationPreferenceResponse preference in preferences)
        {
            NotificationPreference? item = await _context.Preferences.SingleOrDefaultAsync(item => item.AccountId == accountId && item.Type == preference.Type, cancellationToken);
            if (item is null)
            {
                item = new NotificationPreference { AccountId = accountId, Type = preference.Type };
                _context.Preferences.Add(item);
            }
            item.Account = preference.Account;
            item.Email = preference.Email;
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
