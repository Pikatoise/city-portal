using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Notifications;

public sealed class WorkflowNotificationQueue
{
    private readonly NotificationsDbContext _context;
    private readonly IDbContextOutbox _outbox;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    internal WorkflowNotificationQueue(NotificationsDbContext context, IDbContextOutbox outbox, IDataProtectionProvider protection, TimeProvider time)
    {
        _context = context;
        _outbox = outbox;
        _protector = protection.CreateProtector("CityPortal.AccountMail.v1");
        _time = time;
    }

    public async Task QueueAsync(Guid eventId, string type, Guid resourceId, DateTimeOffset createdAtUtc, IReadOnlyCollection<WorkflowRecipient> recipients, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty || resourceId == Guid.Empty || !WorkflowNotificationTypes.Contains(type))
            throw new ArgumentException("A known workflow type and event/resource IDs are required.");
        _context.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await _context.Database.UseTransactionAsync(transaction, cancellationToken);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"notification-event:" + eventId}, 0))", cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        foreach (WorkflowRecipient recipient in recipients.DistinctBy(item => item.AccountId).OrderBy(item => item.AccountId))
        {
            if (await _context.WorkflowReceipts.AnyAsync(item => item.EventId == eventId && item.AccountId == recipient.AccountId && item.Type == type, cancellationToken))
                continue;
            NotificationPreference? preference = await _context.Preferences.AsNoTracking().SingleOrDefaultAsync(item => item.AccountId == recipient.AccountId && item.Type == type, cancellationToken);
            var id = Guid.NewGuid();
            _context.WorkflowReceipts.Add(new WorkflowReceipt { EventId = eventId, AccountId = recipient.AccountId, Type = type, NotificationId = id });
            if (preference?.Account != false && createdAtUtc.AddDays(30) > now)
                _context.AccountNotifications.Add(new AccountNotification { Id = id, AccountId = recipient.AccountId, Type = type, ResourceId = resourceId, CreatedAtUtc = createdAtUtc });
            if (preference?.Email != false && createdAtUtc.AddDays(7) > now)
            {
                _context.Deliveries.Add(new EmailDelivery
                {
                    Id = id,
                    AccountId = recipient.AccountId,
                    Purpose = "workflow",
                    NotificationType = type,
                    CreatedAtUtc = createdAtUtc,
                    ExpiresAtUtc = createdAtUtc.AddDays(7),
                    ProtectedPayload = _protector.Protect(JsonSerializer.Serialize(new AccountMailPayload { Recipient = recipient.Email, WorkflowType = type }))
                });
                _outbox.Enroll(_context);
                await _outbox.PublishAsync(new DeliverAccountMail { DeliveryId = id, Attempt = 0 });
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FlushAfterCommitAsync()
    {
        await _context.Database.UseTransactionAsync(null);
        await _outbox.FlushOutgoingMessagesAsync();
    }
}
