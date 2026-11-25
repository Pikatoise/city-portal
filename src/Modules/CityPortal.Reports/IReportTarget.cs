using CityPortal.Identity;

namespace CityPortal.Reports;

public interface IReportTarget
{
    string Kind { get; }

    Task<ReportTarget?> FindAsync(Guid id, bool publicOnly, CancellationToken cancellationToken);

    Task HideAsync(AuthorizedAccountScope scope, Guid id, long version, string reason, CancellationToken cancellationToken);

    Task AfterHideCommitAsync() => Task.CompletedTask;
}
