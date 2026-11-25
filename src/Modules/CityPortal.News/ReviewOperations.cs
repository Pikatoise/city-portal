using System.Data.Common;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

public sealed class ReviewOperations : IAccountRoleChangeObserver
{
    private readonly NewsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly AccountOperations _accounts;
    private readonly MediaReferences _media;
    private readonly WorkflowNotificationQueue _notifications;
    private readonly TimeProvider _time;
    private readonly SiteInstallation _installation;
    private readonly TimeZoneInfo _timeZone;
    private static readonly string[] _roles = ["Author", "Editor", "Administrator"];

    internal ReviewOperations(NewsDbContext context, AccountAuthorization authorization, AccountOperations accounts, MediaReferences media, WorkflowNotificationQueue notifications, TimeProvider time, SiteInstallation installation, TimeZoneInfo timeZone)
    {
        _context = context;
        _authorization = authorization;
        _accounts = accounts;
        _media = media;
        _notifications = notifications;
        _time = time;
        _installation = installation;
        _timeZone = timeZone;
    }

    public async Task<NewsPage<ReviewSummaryResponse>> QueueAsync(Guid actorId, Guid sessionId, bool mine, int page, int pageSize, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        NewsDatabase.Page(page, pageSize);
        var query = from article in _context.Articles.AsNoTracking()
                    join revision in _context.Revisions.AsNoTracking() on article.LastSubmittedRevisionId equals revision.Id
                    join working in _context.Revisions.AsNoTracking() on article.WorkingRevisionId equals working.Id
                    where mine ? article.ReviewerId == actorId : article.DeletedAtUtc == null && article.WorkingRevisionId == revision.Id && (revision.State == "InReview" || revision.State == "Approved")
                    select new { Article = article, Revision = revision, Working = working };
        int total = await query.CountAsync(cancellationToken);
        ReviewSummaryResponse[] items = await query.OrderByDescending(item => item.Article.UpdatedAtUtc).ThenBy(item => item.Article.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new ReviewSummaryResponse(item.Article.Id, item.Article.Version, item.Revision.Title, item.Article.DeletedAtUtc.HasValue ? "Deleted" : item.Working.State, item.Article.ReviewerId, item.Article.UpdatedAtUtc)).ToArrayAsync(cancellationToken);
        return new NewsPage<ReviewSummaryResponse>(items, total, page, pageSize);
    }

    public async Task<NewsPage<ArticleSummaryResponse>> PublicationsAsync(Guid actorId, Guid sessionId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        NewsDatabase.Page(page, pageSize);
        var query = from article in _context.Articles.AsNoTracking()
                    join revision in _context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
                    where article.PublicationState == "Published" && article.DeletedAtUtc == null
                    select new { Article = article, Revision = revision };
        int total = await query.CountAsync(cancellationToken);
        ArticleSummaryResponse[] items = await query.OrderByDescending(item => item.Article.FirstPublishedAtUtc).ThenBy(item => item.Article.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new ArticleSummaryResponse(item.Article.Id, item.Article.Version, item.Revision.Title, "Published", item.Article.UpdatedAtUtc, item.Article.Featured, item.Article.PublicAddress)).ToArrayAsync(cancellationToken);
        return new NewsPage<ArticleSummaryResponse>(items, total, page, pageSize);
    }

    public async Task<ArticleResponse> PublicationAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        var row = await (from article in _context.Articles.AsNoTracking()
                         join revision in _context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
                         where article.Id == id && article.DeletedAtUtc == null && article.PublicationState == "Published"
                         select new { Article = article, Revision = revision }).SingleOrDefaultAsync(cancellationToken)
            ?? throw NewsDatabase.Missing();
        return ArticleOperations.Response(row.Article, row.Revision);
    }

    public async Task<ArticleResponse> CorrectAsync(Guid actorId, Guid sessionId, Guid id, long version, ArticleContent content, string? reason, CancellationToken cancellationToken)
    {
        ValidatedArticleContent validated = ArticleDocuments.Validate(content, requireComplete: true);
        string feedback = ArticleDocuments.Text(reason, 2000, "reason");
        if (feedback.Length == 0)
            throw NewsDatabase.Invalid("reason", "An editorial correction reason is required.");
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        NewsArticle article = await LockAsync(context, id, version, scope.Actor, "correct", cancellationToken);
        if (article.PublicationState != "Published" || !article.PublishedRevisionId.HasValue)
            throw NewsDatabase.Conflict();
        ArticleRevision published = await context.Revisions.SingleAsync(item => item.Id == article.PublishedRevisionId && item.ArticleId == id, cancellationToken);
        await ArticleOperations.ValidateTaxonomyAsync(context, validated.Content, JsonSerializer.Deserialize<ArticleContent>(published.ContentJson)!, cancellationToken);
        await SetAddressAsync(context, article, validated.Content.Title, cancellationToken);
        ArticleRevision working = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == id, cancellationToken);
        article.Version++;
        article.UpdatedAtUtc = _time.GetUtcNow();
        bool replaceWorking = working.Id == published.Id || working.State is "Draft" or "InReview" or "Approved";
        if (working.Id != published.Id && replaceWorking)
        {
            working.Retained = true;
            working.State = "Superseded";
            await RecordAsync(context, article, working, actorId, "supersede", feedback, scope.Connection, scope.Transaction, article.ReviewerId, cancellationToken);
        }
        var correction = new ArticleRevision { Id = Guid.NewGuid(), ArticleId = id, State = "Published", Retained = true, SubmittedAtUtc = article.UpdatedAtUtc, CreatedAtUtc = article.UpdatedAtUtc };
        ArticleOperations.Apply(correction, validated);
        context.Revisions.Add(correction);
        article.PublishedRevisionId = correction.Id;
        if (replaceWorking)
        {
            article.WorkingRevisionId = correction.Id;
            article.LastSubmittedRevisionId = correction.Id;
        }
        await _media.ReplaceAsync(scope.Connection, scope.Transaction, correction.Id, article.AuthorId, article.ReviewerId, true, true,
            validated.ImageIds, cancellationToken, inheritedRevisionId: published.Id, selectionOwnerId: actorId);
        await RecordAsync(context, article, correction, actorId, "correct", feedback, scope.Connection, scope.Transaction, article.ReviewerId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await RefreshMediaAsync(context, article, scope.Connection, scope.Transaction, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await FlushAfterCommitAsync();
        return ArticleOperations.Response(article, correction);
    }

    public async Task<ReviewResponse> GetAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        NewsArticle article = await _context.Articles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NewsDatabase.Missing();
        RequireHistory(actor, article);
        ArticleRevision revision = await _context.Revisions.AsNoTracking().SingleAsync(item => item.Id == article.LastSubmittedRevisionId && item.ArticleId == id, cancellationToken);
        string workingState = await _context.Revisions.Where(item => item.Id == article.WorkingRevisionId).Select(item => item.State).SingleAsync(cancellationToken);
        return new ReviewResponse(ArticleOperations.Response(article, revision), article.AuthorId, article.ReviewerId, article.PublicationState, article.DeletedAtUtc.HasValue, workingState);
    }

    public async Task<NewsPage<ArticleHistoryResponse>> HistoryAsync(Guid actorId, Guid sessionId, Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken, allowBlockedOwnerRead: true);
        NewsDatabase.Page(page, pageSize);
        NewsArticle article = await _context.Articles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NewsDatabase.Missing();
        bool owner = actor.Roles.Contains("Author") && article.AuthorId == actorId && !article.DeletedAtUtc.HasValue;
        if (!owner)
        {
            if (actor.BlockReason is not null)
                throw NewsDatabase.Missing();
            RequireHistory(actor, article);
        }
        var query = from entry in _context.History.AsNoTracking()
                    join revision in _context.Revisions.AsNoTracking() on entry.RevisionId equals revision.Id
                    where entry.ArticleId == id && (owner || revision.SubmittedAtUtc != null || article.DeletedAtUtc != null)
                    select new { Entry = entry, Revision = revision };
        int total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.Entry.CreatedAtUtc).ThenBy(item => item.Entry.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new NewsPage<ArticleHistoryResponse>(rows.Select(item => new ArticleHistoryResponse(item.Entry.Id, item.Entry.ActorId, item.Entry.Action, item.Entry.Reason,
            item.Entry.ReviewerId, item.Entry.Version, item.Entry.CreatedAtUtc, ArticleOperations.Response(article, item.Revision))).ToArray(), total, page, pageSize);
    }

    public async Task<ArticleResponse> ActAsync(Guid actorId, Guid sessionId, Guid id, long version, string action, string? reason, CancellationToken cancellationToken)
    {
        if (action is not ("submit" or "recall" or "claim" or "release" or "takeover" or "return" or "reject" or "approve" or "publish" or "propose" or "withdraw"))
            throw NewsDatabase.Missing();
        reason = ArticleDocuments.Text(reason, 2000, "reason");
        string[] roles = action is "submit" or "recall" or "propose" ? ["Author"] : action == "publish" ? _roles : ["Editor", "Administrator"];
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, roles, cancellationToken);
        ArticleResponse result = await ActAsync(scope, id, version, action, reason, reported: false, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await FlushAfterCommitAsync();
        return result;
    }

    internal async Task WithdrawReportedAsync(AuthorizedAccountScope scope, Guid id, long version, string reason, CancellationToken cancellationToken)
    {
        await ActAsync(scope, id, version, "withdraw", reason, reported: true, cancellationToken);
    }

    private async Task<ArticleResponse> ActAsync(AuthorizedAccountScope scope, Guid id, long version, string action,
        string reason, bool reported, CancellationToken cancellationToken)
    {
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        Guid actorId = scope.Actor.Id;
        NewsArticle article = await LockAsync(context, id, version, scope.Actor, reported ? "report-withdraw" : action, cancellationToken);
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == id, cancellationToken);
        Guid? previousReviewer = article.ReviewerId;
        Guid? firstPublicationRecipient = action == "publish" && article.FirstPublishedAtUtc is null ? article.IdeaContributorId : null;
        if (action is "submit" or "recall" && article.AuthorId != actorId)
            throw NewsDatabase.Missing();
        switch (action)
        {
            case "propose":
                if (article.PublicationState != "Published" || article.WorkingRevisionId != article.PublishedRevisionId)
                    throw NewsDatabase.Conflict();
                await DraftAsync(context, article, revision, scope, cancellationToken);
                break;
            case "withdraw":
                if (article.PublicationState != "Published")
                    throw NewsDatabase.Conflict();
                if (reason.Length == 0)
                    throw NewsDatabase.Invalid("reason", "A withdrawal reason is required.");
                article.PublicationState = "Withdrawn";
                article.Featured = false;
                revision.Retained = true;
                await DraftAsync(context, article, revision, scope, cancellationToken);
                break;
            case "submit":
                RequireState(revision, "Draft");
                if (article.LastSubmittedRevisionId.HasValue && reason.Length == 0)
                    throw NewsDatabase.Invalid("reason", "Describe changes before resubmitting.");
                await SubmitAsync(context, article, revision, ArticleDocuments.Validate(JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson)!, requireComplete: true), scope, cancellationToken);
                break;
            case "recall":
                RequireState(revision, "InReview");
                await DraftAsync(context, article, revision, scope, cancellationToken);
                break;
            case "claim":
            case "takeover":
                RequireState(revision, "InReview");
                if (action == "claim" ? article.ReviewerId.HasValue : !article.ReviewerId.HasValue || article.ReviewerId == actorId)
                    throw NewsDatabase.Conflict();
                article.ReviewerId = actorId;
                break;
            case "release":
                RequireReviewer(article, actorId);
                RequireState(revision, "InReview");
                article.ReviewerId = null;
                break;
            case "return":
            case "reject":
            case "approve":
                RequireReviewer(article, actorId);
                RequireState(revision, "InReview");
                if (action != "approve" && reason.Length == 0)
                    throw NewsDatabase.Invalid("reason", "Feedback or a rejection reason is required.");
                if (action == "return")
                    await DraftAsync(context, article, revision, scope, cancellationToken);
                else
                    revision.State = action == "reject" ? "FinalRejected" : "Approved";
                break;
            case "publish":
                RequireState(revision, "Approved");
                if (!(article.AuthorId == actorId && scope.Actor.Roles.Contains("Author")) && !Editor(scope.Actor))
                    throw NewsDatabase.Missing();
                ArticleDocuments.Validate(JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson)!, requireComplete: true);
                article.PublicationState = "Published";
                article.PublishedRevisionId = revision.Id;
                article.FirstPublishedAtUtc ??= _time.GetUtcNow();
                await SetAddressAsync(context, article, revision.Title, cancellationToken);
                revision.State = "Published";
                break;
        }
        article.Version++;
        article.UpdatedAtUtc = _time.GetUtcNow();
        ArticleRevision working = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId, cancellationToken);
        // Events reference the submitted snapshot, never a mutable returned draft.
        ArticleRevision snapshot = action == "withdraw"
            ? await context.Revisions.SingleAsync(item => item.Id == article.PublishedRevisionId, cancellationToken)
            : action == "submit" ? working : revision;
        await RecordAsync(context, article, snapshot, actorId, action, reason, scope.Connection, scope.Transaction, previousReviewer,
            cancellationToken, firstPublicationRecipient);
        await RefreshMediaAsync(context, article, scope.Connection, scope.Transaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return ArticleOperations.Response(article, action == "withdraw" && !(actorId == article.AuthorId && scope.Actor.Roles.Contains("Author")) ? snapshot : working);
    }

    private async Task SetAddressAsync(NewsDbContext context, NewsArticle article, string title, CancellationToken cancellationToken)
    {
        await NewsDatabase.RequireAddressesAsync(context, cancellationToken);
        if (article.FirstPublishedLocalDate is null)
        {
            InstallationInfo site = await _installation.GetAsync(cancellationToken);
            TimeZoneInfo zone = site.TimeZone is null ? _timeZone : TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone);
            article.FirstPublishedLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(article.FirstPublishedAtUtc!.Value, zone).DateTime);
        }
        string address = ArticleUrls.Address(title, article.FirstPublishedLocalDate.Value, article.Id);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({address}, 0))", cancellationToken);
        if (await context.Articles.AnyAsync(item => item.Id != article.Id && item.PublicAddress == address, cancellationToken))
            throw NewsDatabase.Invalid("title", "This public address is already in use. Change the title.");
        article.PublicAddress = address;
    }

    internal async Task<ArticleResponse> SaveApprovedAsync(NewsDbContext context, NewsArticle article, ArticleRevision revision, ArticleContent content, AuthorizedAccountScope scope, CancellationToken cancellationToken)
    {
        ValidatedArticleContent validated = ArticleDocuments.Validate(content, requireComplete: true);
        ArticleContent previous = JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson)!;
        await ArticleOperations.ValidateTaxonomyAsync(context, validated.Content, previous, cancellationToken);
        await SubmitAsync(context, article, revision, validated, scope, cancellationToken);
        article.Version++;
        article.UpdatedAtUtc = _time.GetUtcNow();
        ArticleRevision submitted = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId, cancellationToken);
        await RecordAsync(context, article, submitted, scope.Actor.Id, "submit", "Approved content changed by explicit Save.", scope.Connection, scope.Transaction, article.ReviewerId, cancellationToken);
        await RefreshMediaAsync(context, article, scope.Connection, scope.Transaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return ArticleOperations.Response(article, submitted);
    }

    internal async Task DeleteAsync(NewsDbContext context, NewsArticle article, ArticleRevision revision, Guid actorId,
        DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await RecordAsync(context, article, revision, actorId, "delete", "", connection, transaction, article.ReviewerId, cancellationToken);
        await RefreshMediaAsync(context, article, connection, transaction, cancellationToken);
    }

    private async Task SubmitAsync(NewsDbContext context, NewsArticle article, ArticleRevision previous, ValidatedArticleContent content, AuthorizedAccountScope scope, CancellationToken cancellationToken)
    {
        await ArticleOperations.ValidateTaxonomyAsync(context, content.Content, JsonSerializer.Deserialize<ArticleContent>(previous.ContentJson)!, cancellationToken);
        if (article.ReviewerId.HasValue && !await _accounts.HasEditorialPermissionAsync(article.ReviewerId.Value, cancellationToken))
            article.ReviewerId = null;
        previous.Retained = true;
        var submitted = new ArticleRevision { Id = Guid.NewGuid(), ArticleId = article.Id, State = "InReview", Retained = true, SubmittedAtUtc = _time.GetUtcNow(), CreatedAtUtc = _time.GetUtcNow() };
        ArticleOperations.Apply(submitted, content);
        context.Revisions.Add(submitted);
        article.WorkingRevisionId = submitted.Id;
        article.LastSubmittedRevisionId = submitted.Id;
        await _media.ReplaceAsync(scope.Connection, scope.Transaction, submitted.Id, article.AuthorId, article.ReviewerId, true, false, content.ImageIds, cancellationToken, inheritedRevisionId: previous.Id);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task DraftAsync(NewsDbContext context, NewsArticle article, ArticleRevision submitted, AuthorizedAccountScope scope, CancellationToken cancellationToken)
    {
        var draft = new ArticleRevision { Id = Guid.NewGuid(), ArticleId = article.Id, ContentJson = submitted.ContentJson, Title = submitted.Title, PlainText = submitted.PlainText, PreviewHtml = submitted.PreviewHtml, CreatedAtUtc = _time.GetUtcNow() };
        context.Revisions.Add(draft);
        article.WorkingRevisionId = draft.Id;
        Guid[] images = ArticleDocuments.Validate(JsonSerializer.Deserialize<ArticleContent>(draft.ContentJson)!).ImageIds;
        await _media.ReplaceAsync(scope.Connection, scope.Transaction, draft.Id, article.AuthorId, null, false, false, images, cancellationToken, inheritedRevisionId: submitted.Id);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshMediaAsync(NewsDbContext context, NewsArticle article, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        bool deleted = article.DeletedAtUtc.HasValue;
        bool exposeDeletedHistory = deleted && article.LastSubmittedRevisionId.HasValue;
        bool authorVisible = article.IdeaContributorId is null || !deleted;
        var revisions = await context.Revisions.Where(revision => revision.ArticleId == article.Id)
            .Select(revision => new { revision.Id, revision.SubmittedAtUtc }).ToArrayAsync(cancellationToken);
        RevisionMediaVisibility[] visibility = revisions.Select(revision =>
        {
            bool submitted = revision.SubmittedAtUtc.HasValue || exposeDeletedHistory;
            bool current = !deleted && (revision.Id == article.WorkingRevisionId || revision.Id == article.PublishedRevisionId);
            bool isPublic = current && article.PublicationState == "Published" && revision.Id == article.PublishedRevisionId;
            return new RevisionMediaVisibility(revision.Id, submitted ? article.ReviewerId : null, submitted, isPublic, current, authorVisible);
        }).ToArray();
        await _media.UpdateVisibilityAsync(connection, transaction, visibility, cancellationToken);
    }

    private async Task RecordAsync(NewsDbContext context, NewsArticle article, ArticleRevision revision, Guid actorId, string action, string reason,
        DbConnection connection, DbTransaction transaction, Guid? previousReviewer, CancellationToken cancellationToken, Guid? firstPublicationRecipient = null)
    {
        var entry = new ArticleHistory { Id = Guid.NewGuid(), ArticleId = article.Id, RevisionId = revision.Id, ActorId = actorId, Action = action, Reason = reason, ReviewerId = action is "release" or "roles-released" ? previousReviewer : article.ReviewerId, Version = article.Version, CreatedAtUtc = _time.GetUtcNow() };
        context.History.Add(entry);
        string? authorType = action switch { "return" => "article.returned", "reject" => "article.rejected", "approve" => "article.approved", "publish" => "article.published", "withdraw" => "article.withdrawn", "correct" => "article.corrected", "supersede" => "article.superseded", _ => null };
        if (authorType is not null)
            await NotifyAsync(entry, authorType, [article.AuthorId], connection, transaction, cancellationToken);
        if (firstPublicationRecipient is Guid residentId)
            await NotifyAsync(entry, "suggestion.published", [residentId], connection, transaction, cancellationToken);
        string? editorType = action switch { "submit" => "review.resubmitted", "recall" => "review.withdrawn", "delete" => "review.deleted", "takeover" or "release" or "roles-released" => "review.transferred", _ => null };
        if (editorType is not null)
        {
            Guid[] recipients = action is "takeover" or "roles-released" ? new[] { previousReviewer, article.ReviewerId }.Where(item => item.HasValue).Select(item => item!.Value).ToArray()
                : (action == "submit" ? article.ReviewerId : previousReviewer) is Guid reviewer ? [reviewer] : [];
            await NotifyAsync(entry, editorType, recipients, connection, transaction, cancellationToken);
        }
    }

    private async Task NotifyAsync(ArticleHistory entry, string type, Guid[] ids, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        WorkflowRecipient[] recipients = await _accounts.FindNotificationRecipientsAsync(ids, cancellationToken);
        await _notifications.QueueAsync(entry.Id, type, entry.ArticleId, entry.CreatedAtUtc, recipients, connection, transaction, cancellationToken);
    }

    public async Task RolesChangedAsync(Guid actorId, Guid accountId, string[] oldRoles, string[] newRoles, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (!oldRoles.Any(role => role is "Editor" or "Administrator") || newRoles.Any(role => role is "Editor" or "Administrator"))
            return;
        await using NewsDbContext context = await NewsDatabase.JoinAsync(connection, transaction, cancellationToken);
        NewsArticle[] articles = await context.Articles.FromSqlInterpolated($"SELECT * FROM news.\"Articles\" WHERE \"ReviewerId\" = {accountId} ORDER BY \"Id\" FOR UPDATE").ToArrayAsync(cancellationToken);
        foreach (NewsArticle article in articles)
        {
            article.ReviewerId = null;
            article.Version++;
            article.UpdatedAtUtc = _time.GetUtcNow();
            ArticleRevision submitted = await context.Revisions.SingleAsync(item => item.Id == article.LastSubmittedRevisionId, cancellationToken);
            await RecordAsync(context, article, submitted, actorId, "roles-released", "Effective editorial permissions removed.", connection, transaction, accountId, cancellationToken);
            await RefreshMediaAsync(context, article, connection, transaction, cancellationToken);
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task FlushAfterCommitAsync() => _notifications.FlushAfterCommitAsync();

    private static async Task<NewsArticle> LockAsync(NewsDbContext context, Guid id, long version, AccountResponse actor, string action, CancellationToken cancellationToken)
    {
        NewsArticle article = await context.Articles.FromSqlInterpolated($"SELECT * FROM news.\"Articles\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw NewsDatabase.Missing();
        if (article.DeletedAtUtc.HasValue)
            throw NewsDatabase.Missing();
        bool owner = article.AuthorId == actor.Id && actor.Roles.Contains("Author");
        bool allowed = action switch
        {
            "report-withdraw" => article.AuthorId != actor.Id && actor.Roles.Any(role => role is "Moderator" or "Administrator"),
            "submit" or "recall" or "propose" => owner,
            _ => owner || Editor(actor) && article.LastSubmittedRevisionId.HasValue
        };
        if (!allowed)
            throw NewsDatabase.Missing();
        if (article.Version != version)
            throw NewsDatabase.Conflict();
        return article;
    }

    private static bool Editor(AccountResponse actor) => actor.Roles.Any(role => role is "Editor" or "Administrator");

    private static void RequireHistory(AccountResponse actor, NewsArticle article)
    {
        bool owner = article.AuthorId == actor.Id && actor.Roles.Contains("Author") && !article.DeletedAtUtc.HasValue;
        if (!article.LastSubmittedRevisionId.HasValue || !(owner || actor.Roles.Contains("Administrator") || Editor(actor) && article.ReviewerId == actor.Id))
            throw NewsDatabase.Missing();
    }

    private static void RequireState(ArticleRevision revision, string state)
    {
        if (revision.State != state)
            throw NewsDatabase.Conflict();
    }

    private static void RequireReviewer(NewsArticle article, Guid actorId)
    {
        if (article.ReviewerId != actorId)
            throw NewsDatabase.Conflict();
    }
}
