using CityPortal.Identity;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.CitizenSubmissions;

public sealed class SuggestionOperations
{
    private const int _pageSize = 20;
    private readonly SuggestionsDbContext _context;
    private readonly TimeProvider _time;
    private readonly ArticleOperations _articles;

    internal SuggestionOperations(SuggestionsDbContext context, TimeProvider time, ArticleOperations articles)
    {
        _context = context;
        _time = time;
        _articles = articles;
    }

    public async Task<SuggestionPage> ListAsync(Guid residentId, int page, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        Suggestion[] rows = await Active(residentId).AsNoTracking().OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id).Skip((page - 1) * _pageSize).Take(_pageSize + 1).ToArrayAsync(cancellationToken);
        return new SuggestionPage(await ProjectAsync(rows.Take(_pageSize).ToArray(), cancellationToken), rows.Length > _pageSize);
    }

    public async Task<SuggestionResponse> GetAsync(Guid residentId, Guid id, CancellationToken cancellationToken)
    {
        Suggestion? item = await Active(residentId).AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (item is null)
            throw Missing();
        return (await ProjectAsync([item], cancellationToken))[0];
    }

    public async Task<SuggestionResponse> CreateAsync(Guid residentId, SuggestionInput input, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var item = new Suggestion
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Title = input.Title.Trim(),
            Description = input.Description.Trim(),
            EventDate = input.EventDate,
            Location = Optional(input.Location),
            Contact = Optional(input.Contact),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(72)
        };
        _context.Suggestions.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        return ToResponse(item, null);
    }

    public async Task<SuggestionResponse> UpdateAsync(Guid residentId, Guid id, UpdateSuggestionRequest request, CancellationToken cancellationToken)
    {
        SuggestionInput details = request.Details;
        string title = details.Title.Trim();
        string description = details.Description.Trim();
        string? location = Optional(details.Location);
        string? contact = Optional(details.Contact);
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        int updated = await Active(residentId).Where(item => item.Id == id && item.Version == request.Version && item.FirstClaimedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Title, title)
                .SetProperty(item => item.Description, description).SetProperty(item => item.EventDate, details.EventDate)
                .SetProperty(item => item.Location, location).SetProperty(item => item.Contact, contact)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        SuggestionResponse current = await GetAsync(residentId, id, cancellationToken);
        if (updated == 0)
            throw new AccountOperationException(409, "suggestion_conflict", "The suggestion changed or can no longer be edited.");

        await transaction.CommitAsync(cancellationToken);
        return current;
    }

    private IQueryable<Suggestion> Active(Guid residentId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return SuggestionDatabase.Active(_context, now).Where(item => item.ResidentId == residentId);
    }

    private async Task<SuggestionResponse[]> ProjectAsync(Suggestion[] items, CancellationToken cancellationToken)
    {
        Guid[] ids = items.Where(item => item.ArticleId.HasValue).Select(item => item.ArticleId.GetValueOrDefault()).ToArray();
        Dictionary<Guid, SuggestionArticleStatus> articles = await _articles.FindSuggestionArticlesAsync(ids, cancellationToken);
        return items.Select(item => ToResponse(item, articles.GetValueOrDefault(item.ArticleId.GetValueOrDefault()))).ToArray();
    }

    private static SuggestionResponse ToResponse(Suggestion item, SuggestionArticleStatus? article) =>
        new(item.Id, item.Title, item.Description, item.EventDate, item.Location, item.Contact, item.CreatedAtUtc,
            item.PausedAtUtc is null ? item.ExpiresAtUtc : null, item.FirstClaimedAtUtc == null, item.Version,
            article?.HasBeenPublished == true ? "Published" : item.PausedAtUtc.HasValue ? "InProgress" : "Queued", article?.PublicUrl);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AccountOperationException Missing() => new(404, "suggestion_not_found", "The suggestion is unavailable.");
}
