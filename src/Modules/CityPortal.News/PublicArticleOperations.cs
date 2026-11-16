using System.Text;
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

    public async Task<PublicArticleResponse> GetAsync(Guid id, string? slug, CancellationToken cancellationToken)
    {
        // Read visibility and the public snapshot together; working revisions never enter this query.
        var row = await (from article in _context.Articles.AsNoTracking()
                         join revision in _context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
                         where article.Id == id && article.DeletedAtUtc == null && article.FirstPublishedAtUtc != null
                         select new { Article = article, Revision = revision }).SingleOrDefaultAsync(cancellationToken)
            ?? throw NewsDatabase.Missing();
        string currentSlug = Slug(row.Revision.Title);
        if (slug is not null && slug != currentSlug)
        {
            // Published snapshots are immutable and retained, so their actual titles are the alias ledger.
            // Unsubmitted and merely approved titles cannot create public aliases.
            string[] titles = await _context.Revisions.AsNoTracking().Where(item => item.ArticleId == id && item.State == "Published")
                .Select(item => item.Title).Distinct().ToArrayAsync(cancellationToken);
            if (!titles.Any(title => Slug(title) == slug))
                throw NewsDatabase.Missing();
        }
        if (row.Article.PublicationState == "Withdrawn")
            throw new AccountOperationException(404, "article_withdrawn", "The article has been withdrawn.");
        if (row.Article.PublicationState != "Published")
            throw NewsDatabase.Missing();
        DateTimeOffset? publishedAt = await _context.History.AsNoTracking()
            .Where(item => item.ArticleId == id && item.RevisionId == row.Revision.Id && (item.Action == "publish" || item.Action == "correct"))
            .Select(item => (DateTimeOffset?)item.CreatedAtUtc).MaxAsync(cancellationToken);
        string name = await _accounts.GetPublicDisplayNameAsync(row.Article.AuthorId, cancellationToken) ?? "";
        return new PublicArticleResponse(id, currentSlug, $"/news/{id}/{Uri.EscapeDataString(currentSlug)}", slug is not null && slug != currentSlug,
            JsonSerializer.Deserialize<ArticleContent>(row.Revision.ContentJson)!, row.Revision.PreviewHtml, name, row.Article.FirstPublishedAtUtc!.Value,
            publishedAt ?? row.Article.FirstPublishedAtUtc.Value);
    }

    private static string Slug(string title)
    {
        var builder = new StringBuilder();
        bool separator = false;
        foreach (Rune rune in title.Normalize(NormalizationForm.FormC).EnumerateRunes())
            if (Rune.IsLetterOrDigit(rune))
            {
                if (separator && builder.Length > 0)
                    builder.Append('-');
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
                separator = false;
            }
            else
                separator = true;
        return builder.Length == 0 ? "article" : builder.ToString();
    }
}
