using CityPortal.Identity;
using CityPortal.Reports;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

internal sealed class ListingReportTarget : IReportTarget
{
    private readonly ClassifiedsDbContext _context;
    private readonly ListingPublications _publications;
    private readonly TimeProvider _time;

    public ListingReportTarget(ClassifiedsDbContext context, ListingPublications publications, TimeProvider time)
    {
        _context = context;
        _publications = publications;
        _time = time;
    }

    public string Kind => "classifieds";

    public async Task<ReportTarget?> FindAsync(Guid id, bool publicOnly, CancellationToken cancellationToken)
    {
        IQueryable<ListingPublication> visible = ClassifiedsDatabase.Visible(_context, _time.GetUtcNow());
        var item = await (from listing in publicOnly ? visible : _context.Publications.AsNoTracking()
                          join draft in _context.Drafts.AsNoTracking() on listing.ListingId equals draft.Id
                          where listing.ListingId == id
                          select new
                          {
                              listing.OwnerId,
                              listing.Title,
                              listing.Description,
                              draft.Version,
                              listing.HiddenByListingBlock,
                              Visible = visible.Any(publication => publication.ListingId == id)
                          }).SingleOrDefaultAsync(cancellationToken);
        return item is null ? null : new ReportTarget(item.OwnerId, item.Title, item.Description,
            item.Visible ? $"/classifieds/{id:D}" : null, item.Version, !item.HiddenByListingBlock);
    }

    public Task HideAsync(AuthorizedAccountScope scope, Guid id, long version, string reason, CancellationToken cancellationToken)
    {
        if (version is < 1 or > int.MaxValue)
            throw new AccountOperationException(409, "listing_draft_conflict", "The listing has changed.");
        return _publications.BlockAsync(scope, id, (int)version, reason, cancellationToken);
    }
}
