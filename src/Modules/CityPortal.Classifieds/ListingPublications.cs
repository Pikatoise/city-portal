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
        if (draft.SubmittedAtUtc is null)
            throw new AccountOperationException(409, "listing_not_in_review", "The review has already closed.");
        await _authorization.RequireUnblockedOwnerAsync(draft.OwnerId, cancellationToken);

        DateTimeOffset now = _time.GetUtcNow();
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
        context.ReviewDecisions.Add(new ListingReviewDecision
        {
            ListingId = id,
            SubmittedVersion = version,
            ActorId = actorId,
            Outcome = "Approved",
            DecidedAtUtc = now
        });
        draft.SubmittedAtUtc = null;
        draft.ReviewOutcome = "Published";
        draft.UpdatedAtUtc = now;
        draft.Version++;
        await _images.CopyForPublicationAsync(scope, id, publication.RevisionId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    public async Task<PublicListingResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await (from publication in ClassifiedsDatabase.Visible(_context, _time.GetUtcNow())
                          join category in _context.Catalog on publication.CategoryId equals category.Id
                          join district in _context.Catalog on publication.DistrictId equals district.Id
                          where publication.ListingId == id
                          select new { Publication = publication, Category = category.Name, District = district.Name })
            .SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
        ListingPublication current = item.Publication;
        PublishedListingPhoto[] photos = await _images.ReadPublishedAsync(current.RevisionId, cancellationToken);
        await RequireVisibleRevisionAsync(id, current.RevisionId, cancellationToken);
        return new PublicListingResponse(id, current.Title, current.Description, item.Category, item.District, current.Price,
            current.Phone, current.PublishedAtUtc, current.ExpiresAtUtc, photos);
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
        string reason = request.Reason.Trim();
        if (reason.Length is < 1 or > 500 || reason.Contains('\0'))
            throw new AccountOperationException(400, "invalid_listing_block_reason", "Enter a reason of up to 500 characters.");

        await using AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await FindOtherDraftAsync(context, actorId, id, cancellationToken);
        ListingDraftOperations.RequireVersion(draft, request.Version);
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
        draft.ReviewOutcome = "Blocked";
        draft.UpdatedAtUtc = now;
        draft.Version++;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
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
