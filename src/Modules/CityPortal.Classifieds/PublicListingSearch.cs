using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

public sealed class PublicListingSearch
{
    private readonly ClassifiedsDbContext _context;
    private readonly TimeProvider _time;

    internal PublicListingSearch(ClassifiedsDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async Task<ClassifiedsPage<PublicListingSummary>> ListAsync(PublicListingsQuery request, CancellationToken cancellationToken)
    {
        int offset = ClassifiedsDatabase.Offset(request.Page);
        if (request.MinPrice > request.MaxPrice)
            throw new AccountOperationException(400, "invalid_price_range", "The maximum price must be at least the minimum price.");
        string search = request.Query?.Trim() ?? "";
        if (search.Contains('\0'))
            throw new AccountOperationException(400, "invalid_listing_query", "Remove unsupported characters.");

        IQueryable<ListingPublication> query = ClassifiedsDatabase.Visible(_context, _time.GetUtcNow());
        if (request.CategoryId is Guid categoryId)
            query = query.Where(item => item.CategoryId == categoryId);
        if (request.DistrictId is Guid districtId)
            query = query.Where(item => item.DistrictId == districtId);
        if (request.MinPrice is long minimum)
            query = query.Where(item => item.Price >= minimum);
        if (request.MaxPrice is long maximum)
            query = query.Where(item => item.Price <= maximum);

        if (search.Length > 0)
            query = query.Where(item => item.SearchVector.Matches(EF.Functions.PlainToTsQuery("russian", search)));
        var listings = from item in query
                       join category in _context.Catalog on item.CategoryId equals category.Id
                       join district in _context.Catalog on item.DistrictId equals district.Id
                       select new { Item = item, Category = category.Name, District = district.Name };
        var ordered = search.Length == 0
            ? listings.OrderByDescending(row => row.Item.PublishedAtUtc)
            : listings.OrderByDescending(row => row.Item.SearchVector.Rank(EF.Functions.PlainToTsQuery("russian", search)))
                .ThenByDescending(row => row.Item.PublishedAtUtc);
        PublicListingSummary[] rows = await ordered.ThenBy(row => row.Item.ListingId)
            .Skip(offset).Take(ClassifiedsDatabase.PageSize + 1)
            .Select(row => new PublicListingSummary(row.Item.ListingId, row.Item.Title, row.Category, row.District, row.Item.Price))
            .ToArrayAsync(cancellationToken);
        return new ClassifiedsPage<PublicListingSummary>(rows.Take(ClassifiedsDatabase.PageSize).ToArray(), rows.Length > ClassifiedsDatabase.PageSize);
    }

    public async Task<PublicListingFilters> FiltersAsync(CancellationToken cancellationToken)
    {
        IQueryable<ListingPublication> visible = ClassifiedsDatabase.Visible(_context, _time.GetUtcNow());
        var rows = await _context.Catalog.AsNoTracking()
            .Where(entry => !entry.Archived || visible.Any(item => item.CategoryId == entry.Id || item.DistrictId == entry.Id))
            .OrderBy(entry => entry.NormalizedName).ThenBy(entry => entry.Id)
            .Select(entry => new { entry.Kind, Option = new PublicListingFilterOption(entry.Id, entry.Name) }).ToArrayAsync(cancellationToken);
        return new PublicListingFilters(
            rows.Where(row => row.Kind == "categories").Select(row => row.Option).ToArray(),
            rows.Where(row => row.Kind == "districts").Select(row => row.Option).ToArray());
    }
}
