using CityPortal.Identity;
using CityPortal.News;
using CityPortal.Reports;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Comments;

internal sealed class CommentReportTarget : IReportTarget
{
    private readonly CommentsDbContext _context;
    private readonly PublicArticleOperations _articles;
    private readonly TimeProvider _time;

    public CommentReportTarget(CommentsDbContext context, PublicArticleOperations articles, TimeProvider time)
    {
        _context = context;
        _articles = articles;
        _time = time;
    }

    public string Kind => "comments";

    public async Task<ReportTarget?> FindAsync(Guid id, bool publicOnly, CancellationToken cancellationToken)
    {
        ArticleComment? comment = await _context.Comments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.DeletedAtUtc == null, cancellationToken);
        if (comment is null)
            return null;

        PublicArticleReference? article = await _articles.FindReferenceAsync(comment.ArticleId, null, cancellationToken);
        if (article is null || (publicOnly && (article.PublicUrl is null || comment.HiddenAtUtc is not null)))
            return null;

        bool canHide = comment.HiddenAtUtc is null;
        string? url = article.PublicUrl;
        if (url is not null && canHide)
            url += $"#comment-{comment.Id:D}";
        return new ReportTarget(comment.OwnerId, article.Title, comment.Body, url, canHide ? 1 : 2, canHide);
    }

    public async Task HideAsync(AuthorizedAccountScope scope, Guid id, long version, string reason, CancellationToken cancellationToken)
    {
        await using var context = new CommentsDbContext(new DbContextOptionsBuilder<CommentsDbContext>().UseNpgsql(scope.Connection).Options);
        await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        if (version != 1 || await context.Comments.Where(item => item.Id == id && item.DeletedAtUtc == null && item.HiddenAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.HiddenAtUtc, now), cancellationToken) == 0)
            throw new AccountOperationException(409, "comment_moderation_conflict", "Reload the comment before hiding it.");
    }
}
