using Microsoft.EntityFrameworkCore;
using Quartz;

namespace CityPortal.Notifications;

[DisallowConcurrentExecution]
public sealed class EmailRetentionJob : IJob
{
    private readonly NotificationsDbContext _context;
    private readonly TimeProvider _time;

    internal EmailRetentionJob(NotificationsDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        await _context.Deliveries.Where(item => item.Purpose == "workflow" && item.ExpiresAtUtc <= now && item.State != "Sent" && item.State != "Skipped")
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.State, "Skipped").SetProperty(item => item.CompletedAtUtc, now)
                .SetProperty(item => item.ProtectedPayload, "").SetProperty(item => item.NextAttemptAtUtc, (DateTimeOffset?)null).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        DateTimeOffset cutoff = now.AddDays(-30);
        await _context.AccountNotifications.Where(item => item.CreatedAtUtc <= cutoff).ExecuteDeleteAsync(cancellationToken);
        await _context.Deliveries.Where(x => (x.State == "Sent" || x.State == "Skipped") && x.CompletedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
