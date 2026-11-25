using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

public sealed class ListingHistory
{
    private static readonly string[] _staffRoles = ["Moderator", "Administrator"];
    private readonly ClassifiedsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly AccountOperations _accounts;

    internal ListingHistory(ClassifiedsDbContext context, AccountAuthorization authorization, AccountOperations accounts)
    {
        _context = context;
        _authorization = authorization;
        _accounts = accounts;
    }

    public async Task<ClassifiedsPage<ListingHistoryItem>> ReadOwnAsync(Guid ownerId, Guid id, int page, CancellationToken cancellationToken)
    {
        if (!await _context.Drafts.AnyAsync(draft => draft.Id == id && draft.OwnerId == ownerId, cancellationToken))
            throw new AccountOperationException(404, "listing_not_found", "The listing is unavailable.");

        return await ReadAsync(id, page, includeActor: false, cancellationToken);
    }

    public async Task<ClassifiedsPage<ListingHistoryItem>> ReadStaffAsync(Guid actorId, Guid sessionId, Guid? id, int page, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _staffRoles, cancellationToken);
        return await ReadAsync(id, page, includeActor: true, cancellationToken);
    }

    private async Task<ClassifiedsPage<ListingHistoryItem>> ReadAsync(Guid? id, int page, bool includeActor, CancellationToken cancellationToken)
    {
        int offset = ClassifiedsDatabase.Offset(page);
        var reviews = _context.ReviewDecisions.Where(item => id == null || item.ListingId == id)
            .Select(item => new { item.ListingId, Version = item.SubmittedVersion, item.Outcome, item.DecidedAtUtc, item.Reason, item.ActorId });
        var blocks = _context.ListingBlockDecisions.Where(item => id == null || item.ListingId == id)
            .Select(item => new { item.ListingId, item.Version, Outcome = "Blocked", item.DecidedAtUtc, Reason = (string?)item.Reason, item.ActorId });
        ListingHistoryItem[] rows = await reviews.Concat(blocks)
            .OrderByDescending(item => item.DecidedAtUtc).ThenBy(item => item.ListingId).ThenByDescending(item => item.Version)
            .Skip(offset).Take(ClassifiedsDatabase.PageSize + 1)
            .Select(item => new ListingHistoryItem(item.ListingId, item.Version, item.Outcome, item.DecidedAtUtc, item.Reason,
                includeActor ? item.ActorId : null, null, includeActor
                    ? _context.Publications.Where(publication => publication.ListingId == item.ListingId).Select(publication => publication.Title).FirstOrDefault()
                    : _context.Drafts.Where(draft => draft.Id == item.ListingId).Select(draft => draft.Title).FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
        ListingHistoryItem[] items = rows.Take(ClassifiedsDatabase.PageSize).ToArray();
        if (includeActor && items.Length > 0)
        {
            Dictionary<Guid, string> names = await _accounts.GetPublicDisplayNamesAsync(items.Select(item => item.ActorId.GetValueOrDefault())
                .Distinct().ToArray(), cancellationToken);
            items = items.Select(item => item with { ActorName = names.GetValueOrDefault(item.ActorId.GetValueOrDefault()) }).ToArray();
        }
        return new ClassifiedsPage<ListingHistoryItem>(items, rows.Length > ClassifiedsDatabase.PageSize);
    }
}
