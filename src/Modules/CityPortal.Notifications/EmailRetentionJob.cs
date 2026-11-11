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
        DateTimeOffset cutoff = _time.GetUtcNow().AddDays(-30);
        await _context.Deliveries.Where(x => (x.State == "Sent" || x.State == "Skipped") && x.CompletedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
