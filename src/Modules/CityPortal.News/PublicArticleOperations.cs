using System.Text.Json;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

public sealed class PublicArticleOperations
{
    private readonly NewsDbContext _context;
    private readonly AccountOperations _accounts;

    internal PublicArticleOperations(NewsDbContext context, AccountOperations accounts)
    {
        _context = context;
        _accounts = accounts;
    }

    public async Task<PublicArticleResponse> GetAsync(string address, CancellationToken cancellationToken)
    {
        await NewsDatabase.RequireAddressesAsync(_context, cancellationToken);
        // Read visibility and the public snapshot together; working revisions never enter this query.
        var row = await (from article in _context.Articles.AsNoTracking()
                         join revision in _context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
                         where article.PublicAddress == "/news/" + address && article.DeletedAtUtc == null && article.FirstPublishedAtUtc != null
                         select new { Article = article, Revision = revision }).SingleOrDefaultAsync(cancellationToken)
            ?? throw NewsDatabase.Missing();
        Guid id = row.Article.Id;
        if (row.Article.PublicationState == "Withdrawn")
            throw new AccountOperationException(404, "article_withdrawn", "The article has been withdrawn.");
        if (row.Article.PublicationState != "Published")
            throw NewsDatabase.Missing();
        DateTimeOffset? publishedAt = await _context.History.AsNoTracking()
            .Where(item => item.ArticleId == id && item.RevisionId == row.Revision.Id && (item.Action == "publish" || item.Action == "correct"))
            .Select(item => (DateTimeOffset?)item.CreatedAtUtc).MaxAsync(cancellationToken);
        string name = await _accounts.GetPublicDisplayNameAsync(row.Article.AuthorId, cancellationToken) ?? "";
        return new PublicArticleResponse(id, row.Article.PublicAddress!,
            JsonSerializer.Deserialize<ArticleContent>(row.Revision.ContentJson)!, row.Revision.PreviewHtml, name, row.Article.FirstPublishedAtUtc!.Value,
            publishedAt ?? row.Article.FirstPublishedAtUtc.Value);
    }
}
