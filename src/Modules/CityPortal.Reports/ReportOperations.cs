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
    private readonly TimeProvider _time;
    private readonly IEnumerable<IReportTarget> _targets;

    internal ReportOperations(ReportsDbContext context, AccountAuthorization authorization,
        TimeProvider time, IEnumerable<IReportTarget> targets)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
        _targets = targets;
    }

    public async Task SubmitAsync(string kind, Guid id, Guid? actorId, Guid? sessionId, SubmitContentReportRequest request, CancellationToken cancellationToken)
    {
        IReportTarget provider = Target(kind);
        string? text = request.Text?.Trim();
        if (!ReportCategories.Contains(kind, request.Category) || text is { Length: > 500 } || text?.Contains('\0') == true)
            throw new AccountOperationException(400, "invalid_report", "Choose a report category and enter up to 500 characters.");

        if (actorId is Guid idOfActor)
        {
            await using AuthorizedAccountScope scope = await _authorization.BeginContentWriteAsync(
                idOfActor, sessionId.GetValueOrDefault(), shareRoleLock: true, cancellationToken);
            await using var context = new ReportsDbContext(new DbContextOptionsBuilder<ReportsDbContext>().UseNpgsql(scope.Connection).Options);
            await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
            await InsertReportAsync(context, provider, kind, id, request.Category, text, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return;
        }
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AccountAuthorization.LockRolesAsync(_context, shared: true, cancellationToken);
        await InsertReportAsync(_context, provider, kind, id, request.Category, text, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task InsertReportAsync(ReportsDbContext context, IReportTarget provider, string kind, Guid id, string category,
        string? text, CancellationToken cancellationToken)
    {
        // Allocate the sequence only after taking this lock so a later visible report cannot overtake an earlier insert.
        string lockKey = $"reports:{kind}:{id:D}";
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        ReportTarget snapshot = await provider.FindAsync(id, publicOnly: true, cancellationToken) ?? throw Missing();
        ReportedContent? target = await context.Targets.FindAsync([kind, id], cancellationToken);
        if (target is null)
            context.Targets.Add(new ReportedContent
            {
                Kind = kind,
                Id = id,
                OwnerId = snapshot.OwnerId,
                Title = snapshot.Title,
                SnapshotText = kind == "comments" ? snapshot.Text : null
            });
        else
            target.Title = snapshot.Title;
        context.Reports.Add(new ContentReport
        {
            Kind = kind,
            TargetId = id,
            Category = category,
            Text = string.IsNullOrEmpty(text) ? null : text,
            ReceivedAtUtc = _time.GetUtcNow()
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReportQueuePage> ListAsync(string kind, Guid actorId, Guid sessionId, int page, bool history, CancellationToken cancellationToken)
    {
        _ = Target(kind);
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        ReportedContentSummary[] rows = await ReviewableTargets(_context, kind, actorId).AsNoTracking()
            .Where(item => (history && item.DirectDecisionId != null) || item.Reports.Any(report => history == report.DecisionId.HasValue))
            .OrderBy(item => item.Reports.Where(report => history == report.DecisionId.HasValue).Min(report => (long?)report.Sequence)).ThenBy(item => item.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new ReportedContentSummary(item.Id, item.Title, item.Reports.Count, item.Reports.Count(report => report.DecisionId == null)))
            .ToArrayAsync(cancellationToken);
        return new ReportQueuePage(rows.Take(_pageSize).ToArray(), rows.Length > _pageSize);
    }

    public async Task<ReportReviewPreview> GetAsync(string kind, Guid id, Guid actorId, Guid sessionId,
        int resolvedPage, int decisionPage, CancellationToken cancellationToken)
    {
        IReportTarget provider = Target(kind);
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        if (resolvedPage is < 1 or > 100000 || decisionPage is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");
        ReportedContent target = await ReviewableTargets(_context, kind, actorId).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw Missing();
        ReportTarget? snapshot = await provider.FindAsync(id, publicOnly: false, cancellationToken);
        IQueryable<ContentReport> reports = _context.Reports.AsNoTracking().Where(item => item.Kind == kind && item.TargetId == id);
        ContentReportResponse[] pending = await reports.Where(item => item.DecisionId == null)
            .OrderBy(item => item.Sequence).Take(_pageSize + 1)
            .Select(item => new ContentReportResponse(item.Sequence, item.Category, item.Text, item.ReceivedAtUtc, item.DecisionId))
            .ToArrayAsync(cancellationToken);
        ContentReportResponse[] resolved = await reports.Where(item => item.DecisionId != null)
            .OrderByDescending(item => item.Sequence)
            .Skip((resolvedPage - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new ContentReportResponse(item.Sequence, item.Category, item.Text, item.ReceivedAtUtc, item.DecisionId))
            .ToArrayAsync(cancellationToken);
        ReportDecisionResponse[] decisions = await _context.Decisions.AsNoTracking()
            .Where(item => item.Id == target.DirectDecisionId || _context.Reports.Any(report =>
                report.Kind == kind && report.TargetId == id && report.DecisionId == item.Id))
            .OrderByDescending(item => item.DecidedAtUtc).ThenBy(item => item.Id)
            .Skip((decisionPage - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new ReportDecisionResponse(item.Id, item.ActorId, item.Outcome, item.Reason, item.DecidedAtUtc))
            .ToArrayAsync(cancellationToken);
        ContentReportResponse[] pendingBatch = pending.Take(_pageSize).ToArray();
        return new ReportReviewPreview(id, snapshot?.Title ?? target.Title, snapshot?.Text ?? target.SnapshotText, snapshot?.PublicUrl, snapshot?.Version,
            snapshot?.CanHide == true, pendingBatch.LastOrDefault()?.Sequence, [.. pendingBatch, .. resolved.Take(_pageSize)],
            decisions.Take(_pageSize).ToArray(), pending.Length > _pageSize, resolved.Length > _pageSize, decisions.Length > _pageSize);
    }

    public async Task DecideAsync(string kind, Guid id, Guid actorId, Guid sessionId, DecideContentReportsRequest request, CancellationToken cancellationToken)
    {
        IReportTarget provider = Target(kind);
        bool hide = request.Outcome == "hide";
        string reason = request.Reason ?? "";
        if (request.Outcome is not ("hide" or "dismiss") || request.ThroughSequence < 1
            || (hide && (request.TargetVersion is not > 0 || reason.Length is < 1 or > 500 || reason.Contains('\0'))))
            throw new AccountOperationException(400, "invalid_report_decision", "Choose a decision. Hiding content requires a current version and a reason.");

        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        await using var context = new ReportsDbContext(new DbContextOptionsBuilder<ReportsDbContext>().UseNpgsql(scope.Connection).Options);
        await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
        if (!await ReviewableTargets(context, kind, actorId).AnyAsync(item => item.Id == id, cancellationToken))
            throw Missing();
        IQueryable<ContentReport> pending = context.Reports.Where(item => item.Kind == kind && item.TargetId == id && item.DecisionId == null);
        IQueryable<ContentReport> reviewed = pending.Where(item => item.Sequence <= request.ThroughSequence);
        if (!await pending.AnyAsync(item => item.Sequence == request.ThroughSequence, cancellationToken)
            || await reviewed.OrderBy(item => item.Sequence).Skip(_pageSize).AnyAsync(cancellationToken))
            throw new AccountOperationException(409, "report_review_conflict", "Reload the reports before deciding.");
        if (hide)
            await provider.HideAsync(scope, id, request.TargetVersion.GetValueOrDefault(), reason, cancellationToken);

        var decision = new ReportDecision { ActorId = actorId, Outcome = request.Outcome, Reason = hide ? reason : null, DecidedAtUtc = _time.GetUtcNow() };
        context.Decisions.Add(decision);
        await context.SaveChangesAsync(cancellationToken);
        await reviewed
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DecisionId, decision.Id), cancellationToken);
        await scope.CommitAsync(cancellationToken);
        if (hide)
            await provider.AfterHideCommitAsync();
    }

    public async Task HideCommentAsync(Guid id, Guid actorId, Guid sessionId, string? reason, CancellationToken cancellationToken)
    {
        string message = reason?.Trim() ?? "";
        if (message.Length is < 1 or > 500 || message.Contains('\0'))
            throw new AccountOperationException(400, "invalid_report_decision", "Enter a reason of 1 to 500 characters.");

        const string kind = "comments";
        IReportTarget provider = Target(kind);
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        ReportTarget snapshot = await provider.FindAsync(id, publicOnly: true, cancellationToken) ?? throw Missing();
        await using var context = new ReportsDbContext(new DbContextOptionsBuilder<ReportsDbContext>().UseNpgsql(scope.Connection).Options);
        await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
        ReportedContent? target = await context.Targets.FindAsync([kind, id], cancellationToken);
        if (target is null)
        {
            target = new ReportedContent { Kind = kind, Id = id, OwnerId = snapshot.OwnerId, Title = snapshot.Title, SnapshotText = snapshot.Text };
            context.Targets.Add(target);
        }

        await provider.HideAsync(scope, id, snapshot.Version, message, cancellationToken);
        var decision = new ReportDecision { ActorId = actorId, Outcome = "hide", Reason = message, DecidedAtUtc = _time.GetUtcNow() };
        context.Decisions.Add(decision);
        target.DirectDecisionId = decision.Id;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static AccountOperationException Missing() => new(404, "report_target_not_found", "The reported content is unavailable.");

    private static IQueryable<ReportedContent> ReviewableTargets(ReportsDbContext context, string kind, Guid actorId) =>
        context.Targets.Where(item => item.Kind == kind && (kind == "comments" || item.OwnerId != actorId));

    private IReportTarget Target(string kind) => _targets.SingleOrDefault(item => item.Kind == kind) ?? throw Missing();
}
