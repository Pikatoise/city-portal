using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly IEnumerable<IArticleDraftDeletionObserver> _deletionObservers;

    internal ArticleOperations(NewsDbContext context, AccountAuthorization authorization, MediaReferences media, TimeProvider time,
        ReviewOperations reviews, IEnumerable<IArticleDraftDeletionObserver> deletionObservers)
    {
        _context = context;
        _authorization = authorization;
        _media = media;
        _time = time;
        _reviews = reviews;
        _deletionObservers = deletionObservers;
    }

    public async Task<NewsPage<ArticleSummaryResponse>> ListAsync(Guid actorId, Guid sessionId, int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Author"], cancellationToken, allowBlockedOwnerRead: true);
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
        AccountResponse actor = await _authorization.RequireAsync(actorId, sessionId, ["Author", "Editor", "Administrator"], cancellationToken, allowBlockedOwnerRead: true);
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

    /// <summary>Creates an assigned story draft inside the caller's authorized transaction.</summary>
    public async Task<Guid> CreateFromSuggestionAsync(Guid actorId, Guid residentId, string title, string description,
        DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var nodes = new JsonArray();
        string[] lines = description.ReplaceLineEndings("\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0)
                nodes.Add(new JsonObject { ["type"] = "hardBreak" });
            if (lines[index].Length > 0)
                nodes.Add(new JsonObject { ["type"] = "text", ["text"] = lines[index] });
        }
        var document = new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph", ["content"] = nodes })
        };
        // Some historical suggestion titles expand when normalized. The complete
        // title stays on the suggestion and the draft uses the News title limit.
        string draftTitle = string.Concat(title.Normalize(NormalizationForm.FormC).EnumerateRunes().Take(200).Select(rune => rune.ToString()));
        ValidatedArticleContent content = ArticleDocuments.Validate(new ArticleContent { Title = draftTitle, Body = JsonSerializer.SerializeToElement(document) });
        await using NewsDbContext context = await NewsDatabase.JoinAsync(connection, transaction, cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        var article = new NewsArticle { Id = Guid.NewGuid(), AuthorId = actorId, IdeaContributorId = residentId, CreatedAtUtc = now, UpdatedAtUtc = now };
        var revision = new ArticleRevision { Id = Guid.NewGuid(), ArticleId = article.Id, CreatedAtUtc = now };
        article.WorkingRevisionId = revision.Id;
        Apply(revision, content);
        context.Articles.Add(article);
        context.Revisions.Add(revision);
        context.History.Add(new ArticleHistory
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            RevisionId = revision.Id,
            ActorId = actorId,
            Action = "CreatedFromSuggestion",
            Version = article.Version,
            CreatedAtUtc = now
        });
        await context.SaveChangesAsync(cancellationToken);
        return article.Id;
    }

    public async Task<Dictionary<Guid, SuggestionArticleStatus>> FindSuggestionArticlesAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
            return [];
        return await _context.Articles.AsNoTracking().Where(item => ids.Contains(item.Id) && item.IdeaContributorId != null && item.DeletedAtUtc == null)
            .Select(item => new SuggestionArticleStatus(item.Id, item.FirstPublishedAtUtc != null,
                item.PublicationState == "Published" ? item.PublicAddress : null,
                item.FirstPublishedAtUtc == null && _context.Revisions.Any(revision => revision.Id == item.WorkingRevisionId && revision.State == "Draft" && !revision.Retained)))
            .ToDictionaryAsync(item => item.ArticleId, cancellationToken);
    }

    public async Task ImportSuggestionImagesAsync(Guid articleId, Guid authorId, Guid suggestionId, Guid[] ids, DbConnection connection,
        DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using NewsDbContext context = await NewsDatabase.JoinAsync(connection, transaction, cancellationToken);
        NewsArticle? article = await context.Articles.FromSqlInterpolated($"SELECT * FROM news.\"Articles\" WHERE \"Id\" = {articleId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (article is null || article.AuthorId != authorId || article.IdeaContributorId is null || article.DeletedAtUtc.HasValue)
            throw NewsDatabase.Missing();
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId, cancellationToken);
        if (revision.State != "Draft" || revision.Retained || article.FirstPublishedAtUtc.HasValue)
            throw NewsDatabase.Conflict();
        ArticleContent content = JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson)!;
        ValidatedArticleContent original = ArticleDocuments.Validate(content);
        ArticleImage[] additions = ids.Where(id => !original.ImageIds.Contains(id)).Select(id => new ArticleImage(id, "")).ToArray();
        if (additions.Length == 0)
            return;
        ValidatedArticleContent updated = ArticleDocuments.Validate(content with { Gallery = [.. content.Gallery, .. additions] });
        await _media.ReplaceAsync(connection, transaction, revision.Id, authorId, null, false, false,
            updated.ImageIds, cancellationToken, suggestionId: suggestionId);
        Apply(revision, updated);
        article.Version++;
        article.UpdatedAtUtc = _time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ArticleResponse> SaveAsync(Guid actorId, Guid sessionId, Guid id, long version, ArticleContent content, CancellationToken cancellationToken)
    {
        ValidatedArticleContent validated = ArticleDocuments.Validate(content);
        ArticleResponse? draft = await TrySaveDraftAsync(actorId, sessionId, id, version, validated, cancellationToken);
        if (draft is not null)
            return draft;

        // Approved saves can update references across several revisions. Start a new
        // exclusive transaction to preserve their lock order and recheck the version.
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        NewsArticle article = await LockOwnAsync(context, id, actorId, version, cancellationToken);
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == id, cancellationToken);
        if (revision.State != "Approved")
            throw NewsDatabase.Conflict();
        ArticleResponse response = await _reviews.SaveApprovedAsync(context, article, revision, content, scope, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await _reviews.FlushAfterCommitAsync();
        return response;
    }

    private async Task<ArticleResponse?> TrySaveDraftAsync(Guid actorId, Guid sessionId, Guid id, long version, ValidatedArticleContent validated,
        CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(actorId, sessionId, ["Author"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        NewsArticle article = await LockOwnAsync(context, id, actorId, version, cancellationToken);
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == id, cancellationToken);
        if (revision.State == "Approved")
            return null;
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
        await DeleteDraftAsync(context, article, actorId, scope.Connection, scope.Transaction, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await _reviews.FlushAfterCommitAsync();
    }

    /// <summary>Deletes an unpublished suggestion draft inside the caller's authorized transaction.</summary>
    public async Task<bool> DeleteSuggestionDraftAsync(Guid articleId, Guid authorId, Guid actorId, DbConnection connection,
        DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using NewsDbContext context = await NewsDatabase.JoinAsync(connection, transaction, cancellationToken);
        NewsArticle? article = await context.Articles.FromSqlInterpolated($"SELECT * FROM news.\"Articles\" WHERE \"Id\" = {articleId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (article is null || article.AuthorId != authorId || article.IdeaContributorId is null || article.DeletedAtUtc.HasValue)
            throw NewsDatabase.Missing();
        if (article.FirstPublishedAtUtc.HasValue)
            return false;
        await DeleteDraftAsync(context, article, actorId, connection, transaction, cancellationToken);
        return true;
    }

    public Task FlushAfterCommitAsync() => _reviews.FlushAfterCommitAsync();

    private async Task DeleteDraftAsync(NewsDbContext context, NewsArticle article, Guid actorId, DbConnection connection,
        DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.WorkingRevisionId && item.ArticleId == article.Id, cancellationToken);
        article.DeletedAtUtc = _time.GetUtcNow();
        article.UpdatedAtUtc = article.DeletedAtUtc.Value;
        article.Version++;
        revision.Retained = true;
        await _reviews.DeleteAsync(context, article, revision, actorId, connection, transaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        if (article.IdeaContributorId.HasValue)
            foreach (IArticleDraftDeletionObserver observer in _deletionObservers)
                await observer.DraftDeletedAsync(article.Id, article.DeletedAtUtc.Value, connection, transaction, cancellationToken);
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
        JsonSerializer.Deserialize<ArticleContent>(revision.ContentJson) ?? throw new InvalidOperationException("The stored revision is invalid."), revision.PreviewHtml, article.UpdatedAtUtc, article.LastSubmittedRevisionId.HasValue, article.PublicationState, article.PublicationState == "Published" ? article.PublicAddress : null);
}
