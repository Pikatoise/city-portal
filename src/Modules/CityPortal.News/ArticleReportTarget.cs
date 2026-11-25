using CityPortal.Identity;
using CityPortal.Reports;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

internal sealed class ArticleReportTarget : IReportTarget
{
    private readonly NewsDbContext _context;
    private readonly ReviewOperations _reviews;

    public ArticleReportTarget(NewsDbContext context, ReviewOperations reviews)
    {
        _context = context;
        _reviews = reviews;
    }

    public string Kind => "articles";

    public Task<ReportTarget?> FindAsync(Guid id, bool publicOnly, CancellationToken cancellationToken) =>
        (from article in _context.Articles.AsNoTracking()
         join revision in _context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
         where article.Id == id && article.DeletedAtUtc == null
             && (!publicOnly || (article.PublicationState == "Published" && article.PublicAddress != null))
         select new ReportTarget(article.AuthorId, revision.Title, revision.PlainText,
             article.PublicationState == "Published" ? article.PublicAddress : null,
             article.Version, article.PublicationState == "Published")).SingleOrDefaultAsync(cancellationToken);

    public Task HideAsync(AuthorizedAccountScope scope, Guid id, long version, string reason, CancellationToken cancellationToken) =>
        _reviews.WithdrawReportedAsync(scope, id, version, reason, cancellationToken);

    public Task AfterHideCommitAsync() => _reviews.FlushAfterCommitAsync();
}
