using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

public sealed class ListingPublications : IAccountBlockObserver
{
    private static readonly string[] _roles = ["Moderator", "Administrator"];
    private readonly ClassifiedsDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly ListingImages _images;
    private readonly TimeProvider _time;

    internal ListingPublications(ClassifiedsDbContext context, AccountAuthorization authorization, ListingImages images, TimeProvider time)
    {
        _context = context;
        _authorization = authorization;
        _images = images;
        _time = time;
    }

    public async Task PublishAsync(Guid actorId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await FindOtherDraftAsync(context, actorId, id, cancellationToken);
        ListingDraftOperations.RequireVersion(draft, version);
        DateTimeOffset now = _time.GetUtcNow();
        if (draft.EditReviewQueuedAtUtc.HasValue)
        {
            draft.EditReviewQueuedAtUtc = null;
            context.ReviewDecisions.Add(Decision(draft, actorId, "Reviewed", now));
            draft.Version++;
            await context.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return;
        }
        if (draft.SubmittedAtUtc is null)
            throw new AccountOperationException(409, "listing_not_in_review", "The review has already closed.");
        await _authorization.RequireUnblockedOwnerAsync(draft.OwnerId, cancellationToken);

        ListingPublication? existing = await context.Publications.FindAsync([id], cancellationToken);
        if (existing is not null)
        {
            if (!existing.HiddenByListingBlock)
                throw new AccountOperationException(409, "listing_not_in_review", "The review has already closed.");
            await ReplaceRevisionAsync(_images, scope, existing, draft, cancellationToken);
            existing.HiddenByListingBlock = false;
        }
        else
        {
            var publication = new ListingPublication
            {
                ListingId = id,
                RevisionId = Guid.NewGuid(),
                OwnerId = draft.OwnerId,
                Title = draft.Title ?? throw InvalidSubmission(),
                Description = draft.Description ?? throw InvalidSubmission(),
                CategoryId = draft.CategoryId ?? throw InvalidSubmission(),
                DistrictId = draft.DistrictId ?? throw InvalidSubmission(),
                Price = draft.Price,
                Phone = draft.Phone,
                Email = draft.Email,
                PublishedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            };
            context.Publications.Add(publication);
            await _images.CopyForPublicationAsync(scope, id, publication.RevisionId, cancellationToken);
        }
        context.ReviewDecisions.Add(Decision(draft, actorId, "Approved", now));
        draft.SubmittedAtUtc = null;
        draft.ReviewOutcome = "Published";
        draft.UpdatedAtUtc = now;
        draft.Version++;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    internal static async Task ReplaceRevisionAsync(ListingImages images, AuthorizedAccountScope scope, ListingPublication publication,
        ListingDraft draft, CancellationToken cancellationToken)
    {
        Guid previousRevision = publication.RevisionId;
        if (!publication.OriginalRevisionId.HasValue)
        {
            publication.OriginalRevisionId = previousRevision;
            publication.OriginalDetails = JsonSerializer.Serialize(publication.Details());
        }
        publication.RevisionId = Guid.NewGuid();
        publication.Title = draft.Title ?? throw InvalidSubmission();
        publication.Description = draft.Description ?? throw InvalidSubmission();
        publication.CategoryId = draft.CategoryId ?? throw InvalidSubmission();
        publication.DistrictId = draft.DistrictId ?? throw InvalidSubmission();
        publication.Price = draft.Price;
        publication.Phone = draft.Phone;
        publication.Email = draft.Email;
        await images.CopyForPublicationAsync(scope, draft.Id, publication.RevisionId, cancellationToken);
        if (previousRevision != publication.OriginalRevisionId)
            await images.ReleasePublicationAsync(scope, previousRevision, cancellationToken);
    }

    private static ListingReviewDecision Decision(ListingDraft draft, Guid actorId, string outcome, DateTimeOffset now) =>
        new() { ListingId = draft.Id, SubmittedVersion = draft.Version, ActorId = actorId, Outcome = outcome, DecidedAtUtc = now };

    public async Task<PublicListingResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var item = await (from publication in ClassifiedsDatabase.Visible(_context, _time.GetUtcNow())
                              join category in _context.Catalog on publication.CategoryId equals category.Id
                              join district in _context.Catalog on publication.DistrictId equals district.Id
                              where publication.ListingId == id
                              select new { Publication = publication, Category = category.Name, District = district.Name })
                .SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
            ListingPublication current = item.Publication;
            PublishedListingPhoto[] photos = await _images.ReadPublishedAsync(current.RevisionId, cancellationToken);
            // Reload a replaced revision without returning a transient 404 for a still-visible listing.
            if (await ClassifiedsDatabase.Visible(_context, _time.GetUtcNow()).AnyAsync(
                publication => publication.ListingId == id && publication.RevisionId == current.RevisionId
                    && publication.ExpiresAtUtc == current.ExpiresAtUtc, cancellationToken))
                return new PublicListingResponse(id, current.Title, current.Description, item.Category, item.District, current.Price,
                    current.Phone, current.PublishedAtUtc, current.ExpiresAtUtc, photos);
        }
    }

    public async Task<Stream> OpenImageAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        Guid? revisionId = await ClassifiedsDatabase.Visible(_context, _time.GetUtcNow()).Where(item => item.ListingId == id)
            .Select(item => (Guid?)item.RevisionId).SingleOrDefaultAsync(cancellationToken);
        if (revisionId is not Guid current)
            throw Missing();
        Stream image = await _images.OpenPublishedAsync(current, imageId, cancellationToken);
        try
        {
            await RequireVisibleRevisionAsync(id, current, cancellationToken);
            return image;
        }
        catch
        {
            await image.DisposeAsync();
            throw;
        }
    }

    public async Task<ClassifiedsPage<ListingModerationItem>> ListForModerationAsync(Guid actorId, Guid sessionId, int page, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        ListingModerationItem[] rows = await (from publication in _context.Publications.AsNoTracking()
                                              join draft in _context.Drafts on publication.ListingId equals draft.Id
                                              where publication.OwnerId != actorId
                                              orderby publication.PublishedAtUtc descending, publication.ListingId
                                              select new ListingModerationItem(publication.ListingId, publication.Title, draft.Version, publication.HiddenByListingBlock))
            .Skip(ClassifiedsDatabase.Offset(page)).Take(ClassifiedsDatabase.PageSize + 1).ToArrayAsync(cancellationToken);
        return new ClassifiedsPage<ListingModerationItem>(rows.Take(ClassifiedsDatabase.PageSize).ToArray(), rows.Length > ClassifiedsDatabase.PageSize);
    }

    public async Task BlockAsync(Guid actorId, Guid sessionId, Guid id, DecideListingReviewRequest request, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        await BlockAsync(scope, id, request.Version, request.Reason, cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    internal async Task BlockAsync(AuthorizedAccountScope scope, Guid id, int version, string reason, CancellationToken cancellationToken)
    {
        reason = reason.Trim();
        if (reason.Length is < 1 or > 500 || reason.Contains('\0'))
            throw new AccountOperationException(400, "invalid_listing_block_reason", "Enter a reason of up to 500 characters.");

        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        Guid actorId = scope.Actor.Id;
        ListingDraft draft = await FindOtherDraftAsync(context, actorId, id, cancellationToken);
        ListingDraftOperations.RequireVersion(draft, version);
        ListingPublication publication = await context.Publications.SingleOrDefaultAsync(item => item.ListingId == id, cancellationToken) ?? throw Missing();
        if (publication.HiddenByListingBlock)
            throw new AccountOperationException(409, "listing_already_blocked", "The listing is already blocked.");

        DateTimeOffset now = _time.GetUtcNow();
        context.ListingBlockDecisions.Add(new ListingBlockDecision
        {
            ListingId = id,
            Version = draft.Version,
            ActorId = actorId,
            DecidedAtUtc = now,
            Reason = reason
        });
        publication.HiddenByListingBlock = true;
        draft.EditReviewQueuedAtUtc = null;
        draft.ReviewOutcome = "Blocked";
        draft.UpdatedAtUtc = now;
        draft.Version++;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task HideForAccountBlockAsync(Guid accountId, AuthorizedAccountScope scope, CancellationToken cancellationToken)
    {
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        await context.Publications.Where(item => item.OwnerId == accountId && !item.HiddenByAccountBlock)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.HiddenByAccountBlock, true), cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();
        await context.Drafts.Where(item => item.OwnerId == accountId && item.ReviewOutcome == "Published")
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.ReviewOutcome, "Hidden")
                .SetProperty(item => item.UpdatedAtUtc, now).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
    }

    private async Task RequireVisibleRevisionAsync(Guid id, Guid revisionId, CancellationToken cancellationToken)
    {
        if (!await ClassifiedsDatabase.Visible(_context, _time.GetUtcNow())
            .AnyAsync(item => item.ListingId == id && item.RevisionId == revisionId, cancellationToken))
            throw Missing();
    }

    private static async Task<ListingDraft> FindOtherDraftAsync(ClassifiedsDbContext context, Guid actorId, Guid id, CancellationToken cancellationToken) =>
        await context.Drafts.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId != actorId, cancellationToken) ?? throw Missing();

    private static AccountOperationException InvalidSubmission() => new(409, "listing_not_ready", "The submitted listing is incomplete.");

    private static AccountOperationException Missing() => new(404, "listing_not_found", "The listing is unavailable.");
}
