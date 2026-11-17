using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

/// <summary>Public discovery over current publications only; drafts and old revisions never enter these queries.</summary>
public sealed class PublicNewsDiscoveryOperations
{
    private readonly NewsDbContext _context;
    private readonly SiteInstallation _installation;
    private readonly TimeZoneInfo _timeZone;
    private readonly AccountOperations _accounts;

    internal PublicNewsDiscoveryOperations(NewsDbContext context, SiteInstallation installation, TimeZoneInfo timeZone, AccountOperations accounts)
    {
        _context = context;
        _installation = installation;
        _timeZone = timeZone;
        _accounts = accounts;
    }

    public async Task<NewsPage<PublicArticleSummaryResponse>> ListAsync(PublicNewsQuery request, CancellationToken cancellationToken)
    {
        await NewsDatabase.RequireAddressesAsync(_context, cancellationToken);
        NewsDatabase.Page(request.Page, request.PageSize);
        string search = ArticleDocuments.Text(request.Query, 200, "query");
        (DateTimeOffset? start, DateTimeOffset? end) = await RangeAsync(request.FromDate, request.ToDate, cancellationToken);
        var query = from article in _context.Articles.AsNoTracking()
                    join revision in _context.Revisions.AsNoTracking() on article.PublishedRevisionId equals revision.Id
                    where article.PublicationState == "Published" && article.DeletedAtUtc == null && article.FirstPublishedAtUtc != null
                    select new { Article = article, Revision = revision };
        string[] relatedTags = [];
        if (request.Featured)
            query = query.Where(item => item.Article.Featured);
        if (request.RelatedTo is Guid relatedId)
        {
            var source = await query.Where(item => item.Article.Id == relatedId)
                .Select(item => new { item.Revision.CategoryId, item.Revision.Tags }).SingleOrDefaultAsync(cancellationToken) ?? throw NewsDatabase.Missing();
            relatedTags = source.Tags.RootElement.EnumerateArray().Select(tag => tag.GetString()!).ToArray();
            query = query.Where(item => item.Article.Id != relatedId &&
                (EF.Functions.JsonExistAny(item.Revision.Tags, relatedTags) || item.Revision.CategoryId == source.CategoryId));
        }
        if (request.CategoryId is Guid categoryId)
            query = query.Where(item => item.Revision.CategoryId == categoryId);
        if (request.TagId is Guid tagId)
            query = query.Where(item => EF.Functions.JsonExistAny(item.Revision.Tags, new[] { tagId.ToString("D") }));
        if (start is DateTimeOffset from)
            query = query.Where(item => item.Article.FirstPublishedAtUtc >= from);
        if (end is DateTimeOffset to)
            query = query.Where(item => item.Article.FirstPublishedAtUtc < to);
        if (search.Length > 0)
            query = query.Where(item => item.Revision.SearchVector.Matches(EF.Functions.PlainToTsQuery("russian", search)));
        int total = await query.CountAsync(cancellationToken);
        // Rank ties fall back to first publication and then the stable article ID.
        var ordered = request.RelatedTo.HasValue
            ? query.OrderByDescending(item => EF.Functions.JsonExistAny(item.Revision.Tags, relatedTags))
                .ThenByDescending(item => item.Article.FirstPublishedAtUtc).ThenBy(item => item.Article.Id)
            : search.Length == 0
            ? query.OrderByDescending(item => item.Article.FirstPublishedAtUtc).ThenBy(item => item.Article.Id)
            : query.OrderByDescending(item => item.Revision.SearchVector.Rank(EF.Functions.PlainToTsQuery("russian", search)))
                .ThenByDescending(item => item.Article.FirstPublishedAtUtc).ThenBy(item => item.Article.Id);
        var rows = await ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(item => new { item.Article.Id, item.Article.AuthorId, item.Article.PublicAddress, item.Revision.Title, item.Revision.Summary, item.Revision.CategoryId, item.Revision.Tags, item.Revision.CoverMediaId, item.Revision.CoverAlt, item.Article.FirstPublishedAtUtc })
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, string> names = await _accounts.GetPublicDisplayNamesAsync(rows.Select(item => item.AuthorId).Distinct().ToArray(), cancellationToken);
        PublicArticleSummaryResponse[] items = rows.Select(item =>
        {
            Guid[] tags = item.Tags is null ? [] : item.Tags.RootElement.EnumerateArray().Select(tag => tag.GetGuid()).ToArray();
            ArticleImage? cover = item.CoverMediaId is Guid mediaId ? new ArticleImage(mediaId, item.CoverAlt ?? "") : null;
            return new PublicArticleSummaryResponse(item.Id, item.Title, item.Summary, item.PublicAddress!, item.CategoryId, tags, cover, item.FirstPublishedAtUtc!.Value, names.GetValueOrDefault(item.AuthorId, ""));
        }).ToArray();
        return new NewsPage<PublicArticleSummaryResponse>(items, total, request.Page, request.PageSize);
    }

    public async Task<NewsPage<PublicTaxonomyResponse>> TaxonomyAsync(string kind, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (kind is not ("categories" or "tags"))
            throw NewsDatabase.Invalid("kind", "Choose categories or tags.");
        NewsDatabase.Page(page, pageSize);
        IQueryable<TaxonomyEntry> query = _context.Taxonomy.AsNoTracking().Where(entry => entry.Kind == kind &&
            (from article in _context.Articles
             join revision in _context.Revisions on article.PublishedRevisionId equals revision.Id
             where article.PublicationState == "Published" && article.DeletedAtUtc == null &&
                 (kind == "categories" ? revision.CategoryId == entry.Id : EF.Functions.JsonExistAny(revision.Tags, new[] { entry.Id.ToString() }))
             select article.Id).Any());
        int total = await query.CountAsync(cancellationToken);
        PublicTaxonomyResponse[] items = await query.OrderBy(item => item.NormalizedName).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(item => new PublicTaxonomyResponse(item.Id, item.Name)).ToArrayAsync(cancellationToken);
        return new NewsPage<PublicTaxonomyResponse>(items, total, page, pageSize);
    }

    public async Task<PublicHomepageResponse> HomepageAsync(CancellationToken cancellationToken)
    {
        HomepageSelection? selection = await _context.HomepageSelections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        Guid[] ids = selection is null ? [] : new[] { selection.FirstCategoryId, selection.SecondCategoryId }.OfType<Guid>().ToArray();
        PublicTaxonomyResponse[] categories = await _context.Taxonomy.AsNoTracking().Where(item => ids.Contains(item.Id))
            .Select(item => new PublicTaxonomyResponse(item.Id, item.Name)).ToArrayAsync(cancellationToken);
        var blocks = new List<PublicCategoryBlock>();
        foreach (Guid id in ids)
        {
            PublicTaxonomyResponse? category = categories.SingleOrDefault(item => item.Id == id);
            if (category is not null)
                blocks.Add(new PublicCategoryBlock(category, (await ListAsync(new PublicNewsQuery(null, id, null, null, null, 1, 5), cancellationToken)).Items));
        }
        return new PublicHomepageResponse((await ListAsync(new PublicNewsQuery(null, null, null, null, null, 1, 5, Featured: true), cancellationToken)).Items,
            (await ListAsync(new PublicNewsQuery(null, null, null, null, null, 1, 5), cancellationToken)).Items, blocks.ToArray());
    }

    private async Task<(DateTimeOffset? Start, DateTimeOffset? End)> RangeAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (from is DateOnly first && to is DateOnly last && first > last)
            throw NewsDatabase.Invalid("from", "The start date must not be after the end date.");
        if (from is null && to is null)
            return (null, null);
        TimeZoneInfo zone = await ZoneAsync(cancellationToken);
        return (from is DateOnly start ? Midnight(start, zone, "from") : null, to is DateOnly end ? Midnight(end, zone, "to", nextDay: true) : null);
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken cancellationToken)
    {
        InstallationInfo site = await _installation.GetAsync(cancellationToken);
        if (site.TimeZone is null)
            return _timeZone;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return _timeZone;
        }
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone, string field, bool nextDay = false)
    {
        try
        {
            // The end of an inclusive city-local day is the exclusive start of the next local day.
            var local = DateTime.SpecifyKind((nextDay ? date.AddDays(1) : date).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
        }
        catch (ArgumentException)
        {
            throw NewsDatabase.Invalid(field, "Choose a date that exists in the city time zone.");
        }
    }
}
