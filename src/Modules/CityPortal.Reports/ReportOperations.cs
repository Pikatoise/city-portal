using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Reports;

public sealed class ReportOperations
{
    private static readonly string[] _roles = ["Moderator", "Administrator"];
    private const int _pageSize = 20;
    private readonly ReportsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly AccountOperations _accounts;
    private readonly TimeProvider _time;
    private readonly IEnumerable<IReportTarget> _targets;

    internal ReportOperations(ReportsDbContext context, AccountAuthorization authorization, AccountOperations accounts,
        TimeProvider time, IEnumerable<IReportTarget> targets)
    {
        _context = context;
        _authorization = authorization;
        _accounts = accounts;
        _time = time;
        _targets = targets;
    }

    public async Task SubmitAsync(string kind, Guid id, Guid? actorId, Guid? sessionId, SubmitContentReportRequest request, CancellationToken cancellationToken)
    {
        IReportTarget provider = Target(kind);
        string? text = request.Text?.Trim();
        if (!ReportCategories.Contains(kind, request.Category) || text is { Length: > 500 } || text?.Contains('\0') == true)
            throw new AccountOperationException(400, "invalid_report", "Choose a report category and enter up to 500 characters.");

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AccountAuthorization.LockRolesAsync(_context, shared: true, cancellationToken);
        if (actorId.HasValue)
        {
            AccountResponse? actor = sessionId is Guid session ? await _accounts.FindSessionAsync(session, cancellationToken) : null;
            if (actor?.Id != actorId)
                throw new AccountOperationException(401, "invalid_session", "Log in again.");
            if (actor?.BlockReason is not null)
                throw new AccountOperationException(403, "account_blocked", "The account is restricted to reading its content.");
        }
        // Allocate the sequence only after taking this lock so a later visible report cannot overtake an earlier insert.
        string lockKey = $"reports:{kind}:{id:D}";
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        ReportTarget snapshot = await provider.FindAsync(id, publicOnly: true, cancellationToken) ?? throw Missing();
        ReportedContent? target = await _context.Targets.FindAsync([kind, id], cancellationToken);
        if (target is null)
            _context.Targets.Add(new ReportedContent { Kind = kind, Id = id, OwnerId = snapshot.OwnerId, Title = snapshot.Title });
        else
            target.Title = snapshot.Title;
        _context.Reports.Add(new ContentReport
        {
            Kind = kind,
            TargetId = id,
            Category = request.Category,
            Text = string.IsNullOrEmpty(text) ? null : text,
            ReceivedAtUtc = _time.GetUtcNow()
        });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReportQueuePage> ListAsync(string kind, Guid actorId, Guid sessionId, int page, bool history, CancellationToken cancellationToken)
    {
        _ = Target(kind);
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        ReportedContentSummary[] rows = await _context.Targets.AsNoTracking()
            .Where(item => item.Kind == kind && item.OwnerId != actorId && item.Reports.Any(report => history == report.DecisionId.HasValue))
            .OrderBy(item => item.Reports.Where(report => history == report.DecisionId.HasValue).Min(report => report.Sequence)).ThenBy(item => item.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new ReportedContentSummary(item.Id, item.Title, item.Reports.Count, item.Reports.Count(report => report.DecisionId == null)))
            .ToArrayAsync(cancellationToken);
        return new ReportQueuePage(rows.Take(_pageSize).ToArray(), rows.Length > _pageSize);
    }

    public async Task<ReportReviewPreview> GetAsync(string kind, Guid id, Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        IReportTarget provider = Target(kind);
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        ReportedContent target = await _context.Targets.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Kind == kind && item.Id == id && item.OwnerId != actorId, cancellationToken) ?? throw Missing();
        ReportTarget? snapshot = await provider.FindAsync(id, publicOnly: false, cancellationToken);
        ContentReportResponse[] rows = await _context.Reports.AsNoTracking().Where(item => item.Kind == kind && item.TargetId == id)
            .OrderByDescending(item => item.Sequence)
            .Select(item => new ContentReportResponse(item.Sequence, item.Category, item.Text, item.ReceivedAtUtc, item.DecisionId))
            .ToArrayAsync(cancellationToken);
        Guid[] decisionIds = rows.Select(row => row.DecisionId).OfType<Guid>().Distinct().ToArray();
        ReportDecisionResponse[] decisions = await _context.Decisions.AsNoTracking().Where(item => decisionIds.Contains(item.Id))
            .OrderByDescending(item => item.DecidedAtUtc).ThenBy(item => item.Id)
            .Select(item => new ReportDecisionResponse(item.Id, item.ActorId, item.Outcome, item.Reason, item.DecidedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ReportReviewPreview(id, snapshot?.Title ?? target.Title, snapshot?.Text, snapshot?.PublicUrl, snapshot?.Version,
            snapshot?.CanHide == true, rows.FirstOrDefault(row => row.DecisionId is null)?.Sequence, rows, decisions);
    }

    public async Task DecideAsync(string kind, Guid id, Guid actorId, Guid sessionId, DecideContentReportsRequest request, CancellationToken cancellationToken)
    {
        IReportTarget provider = Target(kind);
        bool hide = request.Outcome == "hide";
        string reason = request.Reason?.Trim() ?? "";
        if (request.Outcome is not ("hide" or "dismiss") || request.ThroughSequence < 1
            || (hide && (request.TargetVersion is not > 0 || reason.Length is < 1 or > 500 || reason.Contains('\0'))))
            throw new AccountOperationException(400, "invalid_report_decision", "Choose a decision. Hiding content requires a current version and a reason.");

        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        await using var context = new ReportsDbContext(new DbContextOptionsBuilder<ReportsDbContext>().UseNpgsql(scope.Connection).Options);
        await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
        if (!await context.Targets.AnyAsync(item => item.Kind == kind && item.Id == id && item.OwnerId != actorId, cancellationToken))
            throw Missing();
        IQueryable<ContentReport> pending = context.Reports.Where(item => item.Kind == kind && item.TargetId == id && item.DecisionId == null);
        if (!await pending.AnyAsync(item => item.Sequence == request.ThroughSequence, cancellationToken))
            throw new AccountOperationException(409, "report_review_conflict", "Reload the reports before deciding.");
        if (hide)
            await provider.HideAsync(scope, id, request.TargetVersion.GetValueOrDefault(), reason, cancellationToken);

        var decision = new ReportDecision { ActorId = actorId, Outcome = request.Outcome, Reason = hide ? reason : null, DecidedAtUtc = _time.GetUtcNow() };
        context.Decisions.Add(decision);
        await context.SaveChangesAsync(cancellationToken);
        await pending.Where(item => item.Sequence <= request.ThroughSequence)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DecisionId, decision.Id), cancellationToken);
        await scope.CommitAsync(cancellationToken);
        if (hide)
            await provider.AfterHideCommitAsync();
    }

    private static AccountOperationException Missing() => new(404, "report_target_not_found", "The reported content is unavailable.");

    private IReportTarget Target(string kind) => _targets.SingleOrDefault(item => item.Kind == kind) ?? throw Missing();
}
