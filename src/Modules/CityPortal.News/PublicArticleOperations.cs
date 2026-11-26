using System.Data.Common;
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

    public async Task<PublicArticleReference?> FindReferenceAsync(Guid articleId, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is null)
            return await QueryReferenceAsync(_context, articleId, cancellationToken);

        DbConnection connection = transaction.Connection ?? throw new InvalidOperationException("The article lookup requires an active transaction.");
        await using NewsDbContext context = await NewsDatabase.JoinAsync(connection, transaction, cancellationToken);
        return await QueryReferenceAsync(context, articleId, cancellationToken);
    }

    public Task<Dictionary<Guid, string>> FindPublicUrlsAsync(Guid[] articleIds, CancellationToken cancellationToken) =>
        (from article in PreviouslyPublished(_context)
         join revision in _context.Revisions on article.PublishedRevisionId equals revision.Id
         where articleIds.Contains(article.Id) && article.PublicationState == "Published"
         select new { article.Id, article.PublicAddress })
        .ToDictionaryAsync(item => item.Id, item => item.PublicAddress, cancellationToken);

    private static Task<PublicArticleReference?> QueryReferenceAsync(NewsDbContext context, Guid articleId, CancellationToken cancellationToken) =>
        (from article in PreviouslyPublished(context)
         join revision in context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
         where article.Id == articleId
         select new PublicArticleReference(revision.Title, article.PublicationState == "Published" ? article.PublicAddress : null))
        .SingleOrDefaultAsync(cancellationToken);

    private static IQueryable<NewsArticle> PreviouslyPublished(NewsDbContext context) =>
        context.Articles.AsNoTracking().Where(article => article.DeletedAtUtc == null && article.FirstPublishedAtUtc != null);

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
        ArticleContent content = JsonSerializer.Deserialize<ArticleContent>(row.Revision.ContentJson)
            ?? throw new InvalidOperationException("The stored revision is invalid.");
        PublicTaxonomyResponse[] tags = await GetTagsAsync(content.TagIds, cancellationToken);
        DateTimeOffset? publishedAt = await _context.History.AsNoTracking()
            .Where(item => item.ArticleId == id && item.RevisionId == row.Revision.Id && (item.Action == "publish" || item.Action == "correct"))
            .Select(item => (DateTimeOffset?)item.CreatedAtUtc).MaxAsync(cancellationToken);
        string name = await _accounts.GetPublicDisplayNameAsync(row.Article.AuthorId, cancellationToken) ?? "";
        string? contributor = row.Article.IdeaContributorId is Guid residentId
            ? await _accounts.GetPublicDisplayNameAsync(residentId, cancellationToken)
            : null;
        return new PublicArticleResponse(id, row.Article.PublicAddress!, content, tags,
            row.Revision.PreviewHtml, name, row.Article.FirstPublishedAtUtc!.Value,
            publishedAt ?? row.Article.FirstPublishedAtUtc.Value, contributor);
    }

    private async Task<PublicTaxonomyResponse[]> GetTagsAsync(Guid[] tagIds, CancellationToken cancellationToken)
    {
        if (tagIds.Length == 0)
            return [];

        // Archived associations retain their current label on published revisions.
        Dictionary<Guid, PublicTaxonomyResponse> tags = await _context.Taxonomy.AsNoTracking()
            .Where(entry => entry.Kind == "tags" && tagIds.Contains(entry.Id))
            .Select(entry => new PublicTaxonomyResponse(entry.Id, entry.Name))
            .ToDictionaryAsync(entry => entry.Id, cancellationToken);
        return tagIds.Select(id => tags.GetValueOrDefault(id)).OfType<PublicTaxonomyResponse>().ToArray();
    }
}
