using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

public sealed class TaxonomyOperations
{
    private readonly NewsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;

    internal TaxonomyOperations(NewsDbContext context, AccountAuthorization authorization, TimeProvider time)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
    }

    public async Task<NewsPage<TaxonomyResponse>> ListAsync(Guid actorId, Guid sessionId, string kind, int page, int pageSize, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, ["Author", "Editor", "Administrator"], cancellationToken);
        Kind(kind);
        NewsDatabase.Page(page, pageSize);
        IQueryable<TaxonomyEntry> query = _context.Taxonomy.AsNoTracking().Where(item => item.Kind == kind);
        int total = await query.CountAsync(cancellationToken);
        TaxonomyResponse[] items = await query.OrderBy(item => item.NormalizedName).ThenBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new TaxonomyResponse(item.Id, item.Kind, item.Name, item.Archived, item.Version)).ToArrayAsync(cancellationToken);
        return new NewsPage<TaxonomyResponse>(items, total, page, pageSize);
    }

    public async Task<TaxonomyResponse> SaveAsync(Guid actorId, Guid sessionId, string kind, Guid? id, long version, string name, bool archived, CancellationToken cancellationToken)
    {
        Kind(kind);
        name = ArticleDocuments.Text(name, 100, "name");
        if (name.Length == 0)
            throw NewsDatabase.Invalid("name", "Provide a category or tag name.");
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Editor", "Administrator"], cancellationToken);
        await using NewsDbContext context = await NewsDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        TaxonomyEntry item;
        if (id.HasValue)
        {
            item = await context.Taxonomy.SingleOrDefaultAsync(item => item.Id == id && item.Kind == kind, cancellationToken) ?? throw NewsDatabase.Missing();
            if (item.Version != version)
                throw NewsDatabase.Conflict();
            if (item.Archived && !archived)
                throw NewsDatabase.Conflict();
            item.Version++;
        }
        else
        {
            item = new TaxonomyEntry { Id = Guid.NewGuid(), Kind = kind };
            context.Taxonomy.Add(item);
        }
        string normalized = name.ToUpperInvariant();
        if (await context.Taxonomy.AnyAsync(other => other.Id != item.Id && other.Kind == kind && other.NormalizedName == normalized, cancellationToken))
            throw new AccountOperationException(409, "taxonomy_name_exists", "This name is already used, including archived entries.");
        bool newlyArchived = archived && !item.Archived;
        item.Name = name;
        item.NormalizedName = normalized;
        item.Archived = archived;
        context.TaxonomyChanges.Add(new TaxonomyChange { Id = Guid.NewGuid(), TaxonomyId = item.Id, ActorId = actorId, Action = !id.HasValue ? "Created" : newlyArchived ? "Archived" : "Renamed", Name = item.Name, Archived = item.Archived, Version = item.Version, CreatedAtUtc = _time.GetUtcNow() });
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return new TaxonomyResponse(item.Id, item.Kind, item.Name, item.Archived, item.Version);
    }

    private static void Kind(string kind)
    {
        if (kind is not ("categories" or "tags"))
            throw NewsDatabase.Invalid("kind", "Choose categories or tags.");
    }
}
