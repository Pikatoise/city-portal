using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.CitizenSubmissions;

public sealed class SuggestionOperations
{
    private const int _pageSize = 20;
    private readonly SuggestionsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;
    private readonly ArticleOperations _articles;
    private readonly SuggestionImages _images;

    internal SuggestionOperations(SuggestionsDbContext context, AccountAuthorization authorization, TimeProvider time, ArticleOperations articles, SuggestionImages images)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
        _articles = articles;
        _images = images;
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
        SuggestionResponse response = (await ProjectAsync([item], cancellationToken))[0];
        return response with { Images = await _images.ReadAsync(id, cancellationToken) };
    }

    public async Task<Stream> OpenImageAsync(Guid residentId, Guid suggestionId, Guid imageId, CancellationToken cancellationToken)
    {
        if (!await Active(residentId).AnyAsync(item => item.Id == suggestionId, cancellationToken))
            throw Missing();
        return await _images.OpenAsync(suggestionId, imageId, cancellationToken);
    }

    public async Task<SuggestionResponse> CreateAsync(Guid residentId, Guid sessionId, SuggestionInput input, CancellationToken cancellationToken)
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
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(residentId, sessionId, ["RegularUser"], cancellationToken);
        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        context.Suggestions.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        ImageResponse[] attached = input.ImageIds?.Length > 0
            ? await _images.ReplaceAsync(item.Id, residentId, input.ImageIds, scope.Connection, scope.Transaction, cancellationToken)
            : [];
        await scope.CommitAsync(cancellationToken);
        return ToResponse(item, null) with { Images = attached };
    }

    public async Task<SuggestionResponse> UpdateAsync(Guid residentId, Guid sessionId, Guid id, UpdateSuggestionRequest request, CancellationToken cancellationToken)
    {
        SuggestionInput details = request.Details;
        string title = details.Title.Trim();
        string description = details.Description.Trim();
        string? location = Optional(details.Location);
        string? contact = Optional(details.Contact);
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(residentId, sessionId, ["RegularUser"], cancellationToken);
        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        IQueryable<Suggestion> active = SuggestionDatabase.Active(context, _time.GetUtcNow()).Where(item => item.ResidentId == residentId);
        int updated = await active.Where(item => item.Id == id && item.Version == request.Version && item.FirstClaimedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Title, title)
                .SetProperty(item => item.Description, description).SetProperty(item => item.EventDate, details.EventDate)
                .SetProperty(item => item.Location, location).SetProperty(item => item.Contact, contact)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        if (updated == 0)
        {
            if (!await active.AnyAsync(item => item.Id == id, cancellationToken))
                throw Missing();
            throw new AccountOperationException(409, "suggestion_conflict", "The suggestion changed or can no longer be edited.");
        }

        Suggestion item = await active.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken) ?? throw Missing();
        ImageResponse[] attached = details.ImageIds is null
            ? await _images.ReadAsync(id, cancellationToken)
            : await _images.ReplaceAsync(id, residentId, details.ImageIds, scope.Connection, scope.Transaction, cancellationToken);

        await scope.CommitAsync(cancellationToken);
        return ToResponse(item, null) with { Images = attached };
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
            article?.HasBeenPublished == true ? "Published" : item.PausedAtUtc.HasValue ? "InProgress" : "Queued", article?.PublicUrl, []);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AccountOperationException Missing() => new(404, "suggestion_not_found", "The suggestion is unavailable.");
}
