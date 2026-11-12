using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Notifications;

public sealed class EmailAdministration
{
    private readonly NotificationsDbContext _context;
    private readonly IDbContextOutbox _outbox;
    private readonly IAccountMailLinkValidator _links;
    private readonly TimeProvider _time;

    public static IReadOnlyList<string> States { get; } = Array.AsReadOnly(new[] { "Queued", "Sending", "RetryScheduled", "Sent", "Failed", "Skipped" });

    internal EmailAdministration(NotificationsDbContext context, IDbContextOutbox outbox, IAccountMailLinkValidator links, TimeProvider time)
    {
        _context = context;
        _outbox = outbox;
        _links = links;
        _time = time;
    }

    public async Task<EmailDeliveryPage> ListAsync(string? state, int page, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<EmailDelivery> query = _context.Deliveries.AsNoTracking();
        if (state is not null)
            query = query.Where(delivery => delivery.State == state);
        int total = await query.CountAsync(cancellationToken);
        EmailDeliverySummary[] items = await query.OrderByDescending(delivery => delivery.CreatedAtUtc).ThenBy(delivery => delivery.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(delivery => new EmailDeliverySummary(delivery.Id, delivery.AccountId, delivery.Purpose, delivery.State, delivery.AttemptCount,
                delivery.RetryCount, delivery.CreatedAtUtc, delivery.ExpiresAtUtc, delivery.NextAttemptAtUtc, delivery.ErrorCode, delivery.Version))
            .ToArrayAsync(cancellationToken);
        return new EmailDeliveryPage(items, total, page, pageSize);
    }

    public async Task<EmailReplayResult> ReplayAsync(Guid id, long version, Guid actorId, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        _context.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await _context.Database.UseTransactionAsync(transaction, cancellationToken);
        EmailDelivery? delivery = await _context.Deliveries.FromSqlInterpolated(
            $"SELECT * FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (delivery is null)
            return EmailReplayResult.NotFound;
        if (delivery.Version != version || delivery.State != "Failed")
            return EmailReplayResult.Conflict;
        if (delivery.ExpiresAtUtc <= _time.GetUtcNow() || string.IsNullOrEmpty(delivery.ProtectedPayload)
            || !await _links.IsActiveAsync(delivery.AccountId, delivery.LinkId, cancellationToken))
            return EmailReplayResult.InvalidLink;
        _context.Replays.Add(new EmailReplay { Id = Guid.NewGuid(), ActorId = actorId, DeliveryId = id, CreatedAtUtc = _time.GetUtcNow(), PreviousVersion = version });
        delivery.State = "Queued";
        delivery.RetryCount = 0;
        delivery.NextAttemptAtUtc = null;
        delivery.CompletedAtUtc = null;
        delivery.ErrorCode = null;
        delivery.Version++;
        // AttemptCount stays monotonic: duplicate envelopes from earlier attempts cannot replay work.
        _outbox.Enroll(_context);
        await _outbox.PublishAsync(new DeliverAccountMail { DeliveryId = id, Attempt = delivery.AttemptCount });
        await _context.SaveChangesAsync(cancellationToken);
        return EmailReplayResult.Accepted;
    }

    public async Task FlushAfterCommitAsync()
    {
        await _context.Database.UseTransactionAsync(null);
        await _outbox.FlushOutgoingMessagesAsync();
    }
}
