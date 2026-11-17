using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Media;

public sealed class TemplateNotifications
{
    private readonly MediaDbContext _context;
    private readonly WorkflowNotificationQueue _queue;
    private readonly AccountOperations _accounts;
    private readonly TimeProvider _time;

    internal TemplateNotifications(MediaDbContext context, WorkflowNotificationQueue queue, AccountOperations accounts, TimeProvider time)
    {
        _context = context;
        _queue = queue;
        _accounts = accounts;
        _time = time;
    }

    internal async Task QueueAsync(MediaDbContext context, TemplateEvent entry, CancellationToken cancellationToken)
    {
        string? type = entry.Action switch
        {
            "approve" => "template.approved",
            "reject" => "template.rejected",
            "archive" or "RemovalResolved" => "template.archived",
            "block" => "template.blocked",
            "unblock" => "template.unblocked",
            _ => null
        };
        if (type is not null)
        {
            IDbContextTransaction transaction = context.Database.CurrentTransaction ?? throw new InvalidOperationException("Template decisions require a transaction.");
            WorkflowRecipient[] owners = await _accounts.FindNotificationRecipientsAsync([entry.OwnerId], cancellationToken);
            await _queue.QueueAsync(entry.Id, type, entry.TemplateId, entry.CreatedAtUtc, owners, context.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
            if (entry.Action is "block" or "unblock")
            {
                Guid[] authors = await context.References.Where(reference => reference.ImageId == entry.ImageId && reference.Current).Select(reference => reference.AuthorId).Distinct().ToArrayAsync(cancellationToken);
                WorkflowRecipient[] recipients = await _accounts.FindNotificationRecipientsAsync(authors, cancellationToken);
                await _queue.QueueAsync(entry.Id, entry.Action == "block" ? "image.blocked" : "image.unblocked", entry.ImageId, entry.CreatedAtUtc, recipients, context.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
            }
        }
        entry.NotificationDispatchedAtUtc = _time.GetUtcNow();
    }

    public Task FlushAfterCommitAsync() => _queue.FlushAfterCommitAsync();

    // Resume the durable handoff left by installations upgraded from the initial Media slice.
    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        Guid[] ids = await _context.Events.AsNoTracking().Where(entry => entry.NotificationDispatchedAtUtc == null).OrderBy(entry => entry.CreatedAtUtc).Select(entry => entry.Id).Take(50).ToArrayAsync(cancellationToken);
        foreach (Guid id in ids)
        {
            _context.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            TemplateEvent? entry = await _context.Events.FromSqlInterpolated($"SELECT * FROM media.\"Events\" WHERE \"Id\" = {id} FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(cancellationToken);
            if (entry is null || entry.NotificationDispatchedAtUtc.HasValue)
                continue;
            await QueueAsync(_context, entry, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await _queue.FlushAfterCommitAsync();
        }
    }
}
