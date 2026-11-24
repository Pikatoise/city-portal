using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

public sealed class ClassifiedsCatalog
{
    private readonly ClassifiedsDbContext _context;
    private readonly AccountAuthorization _authorization;

    internal ClassifiedsCatalog(ClassifiedsDbContext context, AccountAuthorization authorization)
    {
        _context = context;
        _authorization = authorization;
    }

    public async Task<ClassifiedsPage<CatalogEntryResponse>> ListAsync(string kind, bool includeArchived, int page, CancellationToken cancellationToken)
    {
        RequireKind(kind);
        CatalogEntryResponse[] entries = await _context.Catalog.AsNoTracking()
            .Where(entry => entry.Kind == kind && (includeArchived || !entry.Archived))
            .OrderBy(entry => entry.Name).ThenBy(entry => entry.Id)
            .Skip(ClassifiedsDatabase.Offset(page)).Take(ClassifiedsDatabase.PageSize + 1)
            .Select(entry => new CatalogEntryResponse(entry.Id, entry.Name, entry.RequiresPrice, entry.Archived, entry.Version))
            .ToArrayAsync(cancellationToken);
        return new ClassifiedsPage<CatalogEntryResponse>(entries.Take(ClassifiedsDatabase.PageSize).ToArray(), entries.Length > ClassifiedsDatabase.PageSize);
    }

    public async Task<CatalogEntryResponse> SaveAsync(Guid actorId, Guid sessionId, string kind, Guid? id, int version,
        SaveClassifiedsCatalogEntryRequest input, CancellationToken cancellationToken)
    {
        RequireKind(kind);
        string name = input.Name.Trim();
        if (name.Length == 0 || name.Contains('\0') || (kind == "districts" && (input.RequiresPrice || input.Archived)))
            throw new AccountOperationException(400, "invalid_classifieds_catalog_entry", "Enter a name and use category settings only for categories.");

        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, ["Administrator"], cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        CatalogEntry entry;
        if (id.HasValue)
        {
            entry = await context.Catalog.SingleOrDefaultAsync(value => value.Kind == kind && value.Id == id, cancellationToken)
                ?? throw new AccountOperationException(404, "classifieds_catalog_entry_not_found", "The catalog entry is unavailable.");
            if (entry.Version != version)
                throw new AccountOperationException(409, "classifieds_catalog_conflict", "The catalog entry has changed.");
            entry.Version++;
        }
        else
        {
            entry = new CatalogEntry { Id = Guid.NewGuid(), Kind = kind };
            context.Catalog.Add(entry);
        }

        string normalizedName = name.ToUpperInvariant();
        if (await context.Catalog.AnyAsync(value => value.Kind == kind && value.NormalizedName == normalizedName && value.Id != entry.Id, cancellationToken))
            throw new AccountOperationException(409, "duplicate_classifieds_catalog_name", "A catalog entry already has this name.");

        entry.Name = name;
        entry.NormalizedName = normalizedName;
        entry.RequiresPrice = input.RequiresPrice;
        entry.Archived = input.Archived;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return new CatalogEntryResponse(entry.Id, entry.Name, entry.RequiresPrice, entry.Archived, entry.Version);
    }

    private static void RequireKind(string kind)
    {
        if (kind is not ("categories" or "districts"))
            throw new AccountOperationException(404, "classifieds_catalog_not_found", "The catalog is unavailable.");
    }
}
