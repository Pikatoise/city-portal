using Quartz;

namespace CityPortal.Media;

[DisallowConcurrentExecution]
public sealed class MediaMaintenanceJob : IJob
{
    private readonly MediaMaintenance _maintenance;
    private readonly TemplateNotifications _notifications;

    public MediaMaintenanceJob(MediaMaintenance maintenance, TemplateNotifications notifications)
    {
        _maintenance = maintenance;
        _notifications = notifications;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        await _maintenance.ProcessPendingAsync(cancellationToken);
        await _maintenance.CleanupAsync(cancellationToken);
        await _notifications.DispatchPendingAsync(cancellationToken);
    }
}
