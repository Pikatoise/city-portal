using CityPortal.Identity;
using CityPortal.Media;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

public sealed class ListingDraftOperations
{
    private readonly ClassifiedsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;
    private readonly ListingImages _images;

    internal ListingDraftOperations(ClassifiedsDbContext context, AccountAuthorization authorization, TimeProvider time, ListingImages images)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
        _images = images;
    }

    public async Task<ClassifiedsPage<ListingDraftResponse>> ListAsync(Guid ownerId, int page, CancellationToken cancellationToken)
    {
        ListingDraft[] rows = await _context.Drafts.AsNoTracking().Where(draft => draft.OwnerId == ownerId)
            .OrderByDescending(draft => draft.UpdatedAtUtc).ThenByDescending(draft => draft.Id)
            .Skip(ClassifiedsDatabase.Offset(page)).Take(ClassifiedsDatabase.PageSize + 1).ToArrayAsync(cancellationToken);
        return new ClassifiedsPage<ListingDraftResponse>(rows.Take(ClassifiedsDatabase.PageSize).Select(Response).ToArray(), rows.Length > ClassifiedsDatabase.PageSize);
    }

    public async Task<ListingDraftResponse> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        ListingDraft draft = await FindAsync(_context, ownerId, id, cancellationToken);
        return Response(draft) with { Photos = await _images.ReadAsync(id, cancellationToken) };
    }

    public async Task<Stream> OpenImageAsync(Guid ownerId, Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        if (!await _context.Drafts.AnyAsync(draft => draft.Id == id && draft.OwnerId == ownerId, cancellationToken))
            throw Missing();
        return await _images.OpenAsync(id, imageId, cancellationToken);
    }

    public async Task<ListingDraftResponse> SaveAsync(Guid ownerId, Guid sessionId, Guid? id, int version, ListingDraftInput details, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(ownerId, sessionId, ["RegularUser"], cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft;
        if (id.HasValue)
        {
            draft = await FindAsync(context, ownerId, id.Value, cancellationToken);
            RequireVersion(draft, version);
            draft.Version++;
        }
        else
        {
            draft = new ListingDraft { Id = Guid.NewGuid(), OwnerId = ownerId };
            context.Drafts.Add(draft);
        }

        if (details.CategoryId != draft.CategoryId)
            await RequireActiveEntryAsync(context, "categories", details.CategoryId, "categoryId", cancellationToken);
        if (details.DistrictId != draft.DistrictId)
            await RequireActiveEntryAsync(context, "districts", details.DistrictId, "districtId", cancellationToken);

        draft.Title = Optional(details.Title);
        draft.Description = Optional(details.Description);
        draft.CategoryId = details.CategoryId;
        draft.DistrictId = details.DistrictId;
        draft.Price = details.Price;
        draft.Phone = Optional(details.Phone);
        draft.Email = Optional(details.Email);
        draft.UpdatedAtUtc = _time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        ListingPhotoResponse[] photos = details.Photos is null
            ? await _images.ReadAsync(draft.Id, cancellationToken)
            : await _images.ReplaceAsync(scope, draft.Id, details.Photos, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(draft) with { Photos = photos };
    }

    public async Task DeleteAsync(Guid ownerId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(ownerId, sessionId, ["RegularUser"], cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await FindAsync(context, ownerId, id, cancellationToken);
        RequireVersion(draft, version);
        await _images.ReplaceAsync(scope, draft.Id, [], cancellationToken);
        context.Drafts.Remove(draft);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static async Task<ListingDraft> FindAsync(ClassifiedsDbContext context, Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        await context.Drafts.SingleOrDefaultAsync(draft => draft.Id == id && draft.OwnerId == ownerId, cancellationToken)
            ?? throw Missing();

    private static AccountOperationException Missing() => new(404, "listing_draft_not_found", "The draft is unavailable.");

    private static void RequireVersion(ListingDraft draft, int version)
    {
        if (draft.Version != version)
            throw new AccountOperationException(409, "listing_draft_conflict", "The draft has changed.");
    }

    private static async Task RequireActiveEntryAsync(ClassifiedsDbContext context, string kind, Guid? id, string field, CancellationToken cancellationToken)
    {
        if (id.HasValue && !await context.Catalog.AnyAsync(entry => entry.Id == id && entry.Kind == kind && !entry.Archived, cancellationToken))
            throw new AccountOperationException(400, "invalid_listing_catalog_entry", "Choose an available catalog entry.",
                new Dictionary<string, string[]> { [field] = ["Choose an available entry."] });
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ListingDraftResponse Response(ListingDraft draft) =>
        new(draft.Id, new ListingDraftInput
        {
            Title = draft.Title,
            Description = draft.Description,
            CategoryId = draft.CategoryId,
            DistrictId = draft.DistrictId,
            Price = draft.Price,
            Phone = draft.Phone,
            Email = draft.Email
        }, draft.UpdatedAtUtc, draft.Version);
}
