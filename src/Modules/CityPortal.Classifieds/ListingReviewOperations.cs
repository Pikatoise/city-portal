using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

public sealed class ListingReviewOperations
{
    internal const string Rejected = "Rejected";
    private const string _returned = "Returned";
    private static readonly string[] _roles = ["Moderator", "Administrator"];
    private readonly ClassifiedsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;
    private readonly ListingImages _images;

    internal ListingReviewOperations(ClassifiedsDbContext context, AccountAuthorization authorization, TimeProvider time, ListingImages images)
    {
        _context = context;
        _authorization = authorization;
        _time = time;
        _images = images;
    }

    public async Task<ClassifiedsPage<ListingReviewQueueItem>> ListAsync(Guid actorId, Guid sessionId, int page, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        ListingReviewQueueItem[] rows = await Pending(actorId).OrderBy(draft => draft.SubmittedAtUtc ?? draft.EditReviewQueuedAtUtc).ThenBy(draft => draft.Id)
            .Skip(ClassifiedsDatabase.Offset(page)).Take(ClassifiedsDatabase.PageSize + 1)
            .Select(draft => new ListingReviewQueueItem(draft.Id, draft.Title ?? string.Empty,
                (draft.SubmittedAtUtc ?? draft.EditReviewQueuedAtUtc).GetValueOrDefault(), draft.EditReviewQueuedAtUtc.HasValue))
            .ToArrayAsync(cancellationToken);
        return new ClassifiedsPage<ListingReviewQueueItem>(rows.Take(ClassifiedsDatabase.PageSize).ToArray(), rows.Length > ClassifiedsDatabase.PageSize);
    }

    public async Task<ListingReviewPreview> GetAsync(Guid actorId, Guid sessionId, Guid id, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        ListingDraft draft = await Pending(actorId).SingleOrDefaultAsync(draft => draft.Id == id, cancellationToken) ?? throw Missing();
        ListingPublication? publication = draft.EditReviewQueuedAtUtc.HasValue
            ? await _context.Publications.AsNoTracking().SingleOrDefaultAsync(item => item.ListingId == id, cancellationToken)
            : null;
        ListingDraftInput? original = publication?.OriginalDetails is string json ? JsonSerializer.Deserialize<ListingDraftInput>(json) : null;
        Guid?[] catalogIds = [draft.CategoryId, draft.DistrictId, original?.CategoryId, original?.DistrictId];
        Dictionary<Guid, string> names = await _context.Catalog.Where(entry => catalogIds.Contains(entry.Id))
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Name, cancellationToken);
        ListingPhotoResponse[] photos = await _images.ReadAsync(id, cancellationToken);
        ListingReviewOriginal? comparison = original is not null && publication?.OriginalRevisionId is Guid revisionId
            ? new ListingReviewOriginal(original, Name(original.CategoryId), Name(original.DistrictId),
                await _images.ReadPublishedAsync(revisionId, cancellationToken))
            : null;
        await RequirePendingVersionAsync(actorId, id, draft.Version, cancellationToken);
        return new ListingReviewPreview(ListingDraftOperations.Response(draft) with { Photos = photos },
            Name(draft.CategoryId), Name(draft.DistrictId))
        {
            Original = comparison
        };

        string Name(Guid? key) => names.GetValueOrDefault(key.GetValueOrDefault(), string.Empty);
    }

    public async Task<Stream> OpenImageAsync(Guid actorId, Guid sessionId, Guid id, Guid imageId, int version, bool original, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        await RequirePendingVersionAsync(actorId, id, version, cancellationToken);
        Stream image;
        if (original)
        {
            Guid? revisionId = await (from draft in Pending(actorId)
                                      join publication in _context.Publications on draft.Id equals publication.ListingId
                                      where draft.Id == id && draft.Version == version && draft.EditReviewQueuedAtUtc.HasValue
                                      select publication.OriginalRevisionId).SingleOrDefaultAsync(cancellationToken);
            if (revisionId is not Guid revision)
                throw Missing();
            image = await _images.OpenPublishedAsync(revision, imageId, cancellationToken);
        }
        else
            image = await _images.OpenAsync(id, imageId, cancellationToken);
        try
        {
            await RequirePendingVersionAsync(actorId, id, version, cancellationToken);
            return image;
        }
        catch
        {
            await image.DisposeAsync();
            throw;
        }
    }

    public async Task DecideAsync(Guid actorId, Guid sessionId, Guid id, DecideListingReviewRequest request, bool reject, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await context.Drafts.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId != actorId, cancellationToken)
            ?? throw Missing();
        ListingDraftOperations.RequireVersion(draft, request.Version);
        if (!draft.SubmittedAtUtc.HasValue)
            throw new AccountOperationException(409, "listing_not_in_review", "The review has already closed.");

        string reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 500 || reason.Contains('\0'))
            throw new AccountOperationException(400, "invalid_listing_review_reason", "Enter a reason of up to 500 characters.");

        DateTimeOffset now = _time.GetUtcNow();
        string outcome = reject ? Rejected : _returned;
        context.ReviewDecisions.Add(new ListingReviewDecision
        {
            ListingId = id,
            ActorId = actorId,
            SubmittedVersion = draft.Version,
            DecidedAtUtc = now,
            Outcome = outcome,
            Reason = reason
        });
        draft.ReviewOutcome = outcome;
        draft.SubmittedAtUtc = null;
        draft.UpdatedAtUtc = now;
        draft.Version++;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private IQueryable<ListingDraft> Pending(Guid actorId) =>
        _context.Drafts.AsNoTracking().Where(draft => draft.OwnerId != actorId
            && (draft.SubmittedAtUtc.HasValue || draft.EditReviewQueuedAtUtc.HasValue));

    private async Task RequirePendingVersionAsync(Guid actorId, Guid id, int version, CancellationToken cancellationToken)
    {
        // Reject photos or metadata fetched across a recall and subsequent resubmission.
        if (!await Pending(actorId).AnyAsync(draft => draft.Id == id && draft.Version == version, cancellationToken))
            throw Missing();
    }

    private static AccountOperationException Missing() => new(404, "listing_review_not_found", "The listing submission is unavailable.");
}
