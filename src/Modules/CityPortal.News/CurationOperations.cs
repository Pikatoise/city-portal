using System.Text.Json;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

public sealed class CurationOperations
{
    private readonly NewsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;

    internal CurationOperations(NewsDbContext context, AccountAuthorization authorization, TimeProvider time)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
    }

    public async Task<HomepageSelectionResponse> GetAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        HomepageSelection selection = await _context.HomepageSelections.AsNoTracking().SingleAsync(cancellationToken);
        return new HomepageSelectionResponse(selection.Version, selection.FirstCategoryId, selection.SecondCategoryId);
    }

    public async Task<HomepageSelectionResponse> SaveAsync(Guid actorId, Guid sessionId, long version, Guid first, Guid second, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        HomepageSelection selection = await context.HomepageSelections.SingleAsync(cancellationToken);
        if (selection.Version != version)
            throw NewsDatabase.Conflict();
        if (first == second || await context.Taxonomy.CountAsync(item => (item.Id == first || item.Id == second) && item.Kind == "categories" && !item.Archived, cancellationToken) != 2)
            throw NewsDatabase.Invalid("categories", "Select two distinct active categories in display order.");
        selection.FirstCategoryId = first;
        selection.SecondCategoryId = second;
        selection.Version++;
        context.CurationChanges.Add(new CurationChange { Id = Guid.NewGuid(), ActorId = actorId, Action = "homepage-categories", SelectionJson = JsonSerializer.Serialize(selection), CreatedAtUtc = _time.GetUtcNow() });
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return new HomepageSelectionResponse(selection.Version, first, second);
    }

    public async Task<ArticleSummaryResponse> FeatureAsync(Guid actorId, Guid sessionId, Guid id, long version, bool featured, CancellationToken cancellationToken)
    {
        // BeginAsync serializes role changes and editorial mutations before the article lock.
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        NewsArticle article = await context.Articles.FromSqlInterpolated($"SELECT * FROM news.\"Articles\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw NewsDatabase.Missing();
        if (article.Version != version || article.PublicationState != "Published" || article.DeletedAtUtc.HasValue)
            throw NewsDatabase.Conflict();
        if (featured && !article.Featured && await context.Articles.CountAsync(item => item.Featured, cancellationToken) >= 5)
            throw NewsDatabase.Invalid("featured", "At most five articles can be selected.");
        article.Featured = featured;
        article.Version++;
        context.CurationChanges.Add(new CurationChange { Id = Guid.NewGuid(), ActorId = actorId, ArticleId = id, Action = featured ? "feature" : "unfeature", SelectionJson = JsonSerializer.Serialize(new { featured, article.Version }), CreatedAtUtc = _time.GetUtcNow() });
        await context.SaveChangesAsync(cancellationToken);
        ArticleRevision revision = await context.Revisions.SingleAsync(item => item.Id == article.PublishedRevisionId, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return new ArticleSummaryResponse(article.Id, article.Version, revision.Title, "Published", article.UpdatedAtUtc, article.Featured);
    }
}
