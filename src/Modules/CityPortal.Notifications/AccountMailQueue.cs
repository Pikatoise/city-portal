using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Notifications;

public sealed class AccountMailQueue
{
    private readonly NotificationsDbContext _context;
    private readonly IDbContextOutbox _outbox;
    private readonly IDataProtector _protector;

    internal AccountMailQueue(NotificationsDbContext context, IDbContextOutbox outbox, IDataProtectionProvider protection)
    {
        _context = context;
        _outbox = outbox;
        _protector = protection.CreateProtector("CityPortal.AccountMail.v1");
    }

    public async Task QueueAsync(AccountMailRequest request, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (request.Purpose != "confirmation")
            throw new ArgumentException("Unsupported account mail purpose.", nameof(request));

        _context.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await _context.Database.UseTransactionAsync(transaction, cancellationToken);
        _context.Deliveries.Add(new EmailDelivery
        {
            Id = request.MessageId,
            AccountId = request.AccountId,
            LinkId = request.LinkId,
            Purpose = request.Purpose,
            ProtectedPayload = _protector.Protect(JsonSerializer.Serialize(new AccountMailPayload
            {
                Recipient = request.Recipient,
                Token = request.Token
            })),
            CreatedAtUtc = request.CreatedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc
        });
        _outbox.Enroll(_context);
        await _outbox.PublishAsync(new DeliverAccountMail { DeliveryId = request.MessageId, Attempt = 0 });
        await _context.SaveChangesAsync(cancellationToken);
    }

    // The caller commits every participating module before allowing transport delivery.
    public async Task FlushAfterCommitAsync()
    {
        await _context.Database.UseTransactionAsync(null);
        await _outbox.FlushOutgoingMessagesAsync();
    }

    public async Task<AccountMailStatus> GetLatestAsync(Guid messageId, CancellationToken cancellationToken)
    {
        EmailDelivery? delivery = await _context.Deliveries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        return new AccountMailStatus
        {
            State = delivery?.State == "Sent" ? "Sent" : delivery is null or { AttemptCount: > 0 } || delivery.State is "Failed" or "Skipped" ? "Error" : "Pending",
            LastAttemptAtUtc = delivery?.LastAttemptAtUtc
        };
    }
}
