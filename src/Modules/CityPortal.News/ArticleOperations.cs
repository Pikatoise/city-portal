using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

public sealed class ArticleOperations
{
    private readonly NewsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly MediaReferences _media;
    private readonly TimeProvider _time;
    private readonly ReviewOperations _reviews;

    internal ArticleOperations(NewsDbContext context, AccountAuthorization authorization, MediaReferences media, TimeProvider time, ReviewOperations reviews)
    {
        _context = context;
        _authorization = authorization;
        _media = media;
        _time = time;
        _reviews = reviews;
    }

    public async Task<NewsPage<ArticleSummaryResponse>> ListAsync(Guid actorId, Guid sessionId, int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Author"], cancellationToken);
        NewsDatabase.Page(page, pageSize);
        string queryText = ArticleDocuments.Text(search, 200, "search");
        var query = from article in _context.Articles.AsNoTracking()
                    join revision in _context.Revisions.AsNoTracking() on article.WorkingRevisionId equals revision.Id
                    where article.AuthorId == actorId && article.DeletedAtUtc == null && (queryText == "" || revision.Title.Contains(queryText))
                    select new { Article = article, Revision = revision };
        int total = await query.CountAsync(cancellationToken);
        ArticleSummaryResponse[] items = await query.OrderByDescending(item => item.Article.UpdatedAtUtc).ThenBy(item => item.Article.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new ArticleSummaryResponse(item.Article.Id, item.Article.Version, item.Revision.Title, item.Revision.State, item.Article.UpdatedAtUtc)).ToArrayAsync(cancellationToken);
        return new NewsPage<ArticleSummaryResponse>(items, total, page, pageSize);
    }

    public async Task<ArticleResponse> GetAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, ["Author", "Editor", "Administrator"], cancellationToken);
        NewsArticle? article = await _context.Articles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.AuthorId == actorId && item.DeletedAtUtc == null, cancellationToken);
        if (article is null || !actor.Roles.Contains("Author"))
            return (await _reviews.GetAsync(actorId, sessionId, id, cancellationToken)).Article;
        ArticleRevision revision = await _context.Revisions.AsNoTracking().SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == article.Id, cancellationToken);
        return Response(article, revision);
    }

    public async Task<ArticleResponse> CreateAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        var article = new NewsArticle { Id = Guid.NewGuid(), AuthorId = actorId, CreatedAtUtc = _time.GetUtcNow(), UpdatedAtUtc = _time.GetUtcNow() };
        var revision = new ArticleRevision { Id = Guid.NewGuid(), ArticleId = article.Id, CreatedAtUtc = article.CreatedAtUtc };
        article.WorkingRevisionId = revision.Id;
        Apply(revision, ArticleDocuments.Validate(new ArticleContent()));
        context.Articles.Add(article);
        context.Revisions.Add(revision);
        context.History.Add(new ArticleHistory { Id = Guid.NewGuid(), ArticleId = article.Id, RevisionId = revision.Id, ActorId = actorId, Action = "Created", Version = article.Version, CreatedAtUtc = article.CreatedAtUtc });
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(article, revision);
    }

    public async Task<ArticleResponse> SaveAsync(Guid actorId, Guid sessionId, Guid id, long version, ArticleContent content, CancellationToken cancellationToken)
    {
        ValidatedArticleContent validated = ArticleDocuments.Validate(content);
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        NewsArticle article = await LockOwnAsync(context, id, actorId, version, cancellationToken);
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == id, cancellationToken);
        if (revision.State == "Approved")
        {
            ArticleResponse response = await _reviews.SaveApprovedAsync(context, article, revision, content, scope, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            await _reviews.FlushAfterCommitAsync();
            return response;
        }
        if (revision.State != "Draft" || revision.Retained)
            throw NewsDatabase.Conflict();
        ArticleContent previous = JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson)!;
        await ValidateTaxonomyAsync(context, validated.Content, previous, cancellationToken);
        await _media.ReplaceAsync(scope.Connection, scope.Transaction, revision.Id, actorId, null, false, false, validated.ImageIds, cancellationToken);
        Apply(revision, validated);
        article.Version++;
        article.UpdatedAtUtc = _time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(article, revision);
    }

    public async Task DeleteAsync(Guid actorId, Guid sessionId, Guid id, long version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        NewsArticle article = await LockOwnAsync(context, id, actorId, version, cancellationToken);
        if (article.PublicationState != "NeverPublished" || article.PublishedRevisionId.HasValue)
            throw NewsDatabase.Conflict();
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == id, cancellationToken);
        article.DeletedAtUtc = _time.GetUtcNow();
        article.UpdatedAtUtc = article.DeletedAtUtc.Value;
        article.Version++;
        revision.Retained = true;
        await _reviews.DeleteAsync(context, article, revision, scope, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await _reviews.FlushAfterCommitAsync();
    }

    private static async Task<NewsArticle> LockOwnAsync(NewsDbContext context, Guid id, Guid actorId, long version, CancellationToken cancellationToken)
    {
        NewsArticle? article = await context.Articles.FromSqlInterpolated($"SELECT * FROM news.\"Articles\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (article is null || article.AuthorId != actorId || article.DeletedAtUtc.HasValue)
            throw NewsDatabase.Missing();
        if (article.Version != version)
            throw NewsDatabase.Conflict();
        return article;
    }

    internal static async Task ValidateTaxonomyAsync(NewsDbContext context, ArticleContent content, ArticleContent previous, CancellationToken cancellationToken)
    {
        Guid[] ids = content.TagIds.Concat(content.CategoryId.HasValue ? [content.CategoryId.Value] : Array.Empty<Guid>()).Distinct().ToArray();
        TaxonomyEntry[] entries = await context.Taxonomy.Where(item => ids.Contains(item.Id)).ToArrayAsync(cancellationToken);
        foreach (Guid id in ids)
        {
            string kind = content.CategoryId == id ? "categories" : "tags";
            TaxonomyEntry? entry = entries.SingleOrDefault(item => item.Id == id && item.Kind == kind);
            bool retained = kind == "categories" ? previous.CategoryId == id : previous.TagIds.Contains(id);
            if (entry is null || entry.Archived && !retained || kind == "categories" && content.TagIds.Contains(id))
                throw NewsDatabase.Invalid("taxonomy", "Select existing active taxonomy; only existing associations may retain archived entries.");
        }
    }

    internal static void Apply(ArticleRevision revision, ValidatedArticleContent validated)
    {
        revision.ContentJson = JsonSerializer.Serialize(validated.Content);
        revision.Title = validated.Content.Title;
        revision.PreviewHtml = validated.PreviewHtml;
        revision.PlainText = validated.PlainText;
    }

    internal static ArticleResponse Response(NewsArticle article, ArticleRevision revision) => new(article.Id, revision.Id, article.Version, revision.State,
        JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson) ?? throw new InvalidOperationException("The stored revision is invalid."), revision.PreviewHtml, article.UpdatedAtUtc, article.LastSubmittedRevisionId.HasValue, article.PublicationState);
}
