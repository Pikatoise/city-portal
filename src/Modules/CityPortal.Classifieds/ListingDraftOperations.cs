using System.ComponentModel.DataAnnotations;
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
        return Response(draft) with
        {
            Photos = await _images.ReadAsync(id, cancellationToken),
            ReviewReason = await ReadReviewReasonAsync(_context, draft, cancellationToken)
        };
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
            RequireEditing(draft);
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
        string? reason = await ReadReviewReasonAsync(context, draft, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(draft) with { Photos = photos, ReviewReason = reason };
    }

    public async Task DeleteAsync(Guid ownerId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(ownerId, sessionId, ["RegularUser"], cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await FindAsync(context, ownerId, id, cancellationToken);
        RequireVersion(draft, version);
        RequireOutsideReview(draft);
        RequireUnpublished(draft);
        await _images.ReplaceAsync(scope, draft.Id, [], cancellationToken);
        context.Drafts.Remove(draft);
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    public async Task<ListingDraftResponse> SubmitAsync(Guid ownerId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(ownerId, sessionId, ["RegularUser"], cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await FindAsync(context, ownerId, id, cancellationToken);
        RequireVersion(draft, version);
        RequireEditing(draft);
        ListingPhotoResponse[] photos = await _images.ReadAsync(id, cancellationToken);
        await ValidateSubmissionAsync(context, draft, photos, cancellationToken);

        DateTimeOffset now = _time.GetUtcNow();
        draft.SubmittedAtUtc = now;
        draft.ReviewOutcome = null;
        draft.UpdatedAtUtc = now;
        draft.Version++;
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(draft) with { Photos = photos };
    }

    public async Task<ListingDraftResponse> RecallAsync(Guid ownerId, Guid sessionId, Guid id, int version, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginSharedAsync(ownerId, sessionId, ["RegularUser"], cancellationToken);
        await using ClassifiedsDbContext context = await ClassifiedsDatabase.JoinAsync(scope, cancellationToken);
        ListingDraft draft = await FindAsync(context, ownerId, id, cancellationToken);
        RequireVersion(draft, version);
        if (!draft.SubmittedAtUtc.HasValue)
            throw new AccountOperationException(409, "listing_not_in_review", "Only a submitted listing can be recalled.");

        draft.SubmittedAtUtc = null;
        draft.UpdatedAtUtc = _time.GetUtcNow();
        draft.Version++;
        await context.SaveChangesAsync(cancellationToken);
        ListingPhotoResponse[] photos = await _images.ReadAsync(id, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Response(draft) with { Photos = photos };
    }

    private static async Task ValidateSubmissionAsync(ClassifiedsDbContext context, ListingDraft draft, ListingPhotoResponse[] photos, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (draft.Title is null)
            errors["title"] = ["Enter a title."];
        if (draft.Description is null)
            errors["description"] = ["Enter a description."];

        bool? requiresPrice = await context.Catalog.Where(entry => entry.Id == draft.CategoryId && entry.Kind == "categories")
            .Select(entry => (bool?)entry.RequiresPrice).SingleOrDefaultAsync(cancellationToken);
        if (requiresPrice is null)
            errors["categoryId"] = ["Choose a category."];
        else if (requiresPrice.Value && draft.Price is null)
            errors["price"] = ["Enter a price for this category."];

        if (!await context.Catalog.AnyAsync(entry => entry.Id == draft.DistrictId && entry.Kind == "districts", cancellationToken))
            errors["districtId"] = ["Choose a district."];

        if (draft.Phone is null && draft.Email is null)
            errors["phone"] = ["Enter a phone number or Email address."];
        else if (draft.Phone is not null && !new PhoneAttribute().IsValid(draft.Phone))
            errors["phone"] = ["Enter a valid phone number."];
        if (draft.Email is not null && !new EmailAddressAttribute().IsValid(draft.Email))
            errors["email"] = ["Enter a valid Email address."];
        if (photos.Any(photo => photo.Image.State != "Ready"))
            errors["photos"] = ["Wait for photo processing or remove unavailable photos."];

        if (errors.Count > 0)
            throw new AccountOperationException(400, "listing_not_ready", "Complete the required fields before submitting.", errors);
    }

    private static async Task<ListingDraft> FindAsync(ClassifiedsDbContext context, Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        await context.Drafts.SingleOrDefaultAsync(draft => draft.Id == id && draft.OwnerId == ownerId, cancellationToken)
            ?? throw Missing();

    private static AccountOperationException Missing() => new(404, "listing_draft_not_found", "The draft is unavailable.");

    internal static void RequireVersion(ListingDraft draft, int version)
    {
        if (draft.Version != version)
            throw new AccountOperationException(409, "listing_draft_conflict", "The draft has changed.");
    }

    private static void RequireEditing(ListingDraft draft)
    {
        RequireOutsideReview(draft);
        RequireUnpublished(draft);
        if (draft.ReviewOutcome == ListingReviewOperations.Rejected)
            throw new AccountOperationException(409, "listing_rejected", "A rejected listing cannot be edited or submitted again.");
    }

    private static void RequireUnpublished(ListingDraft draft)
    {
        if (draft.ReviewOutcome is "Published" or "Blocked" or "Hidden")
            throw new AccountOperationException(409, "listing_already_published", "A published listing cannot be changed through the draft workflow.");
    }

    private static void RequireOutsideReview(ListingDraft draft)
    {
        if (draft.SubmittedAtUtc.HasValue)
            throw new AccountOperationException(409, "listing_in_review", "Recall the listing before changing or deleting it.");
    }

    private static async Task RequireActiveEntryAsync(ClassifiedsDbContext context, string kind, Guid? id, string field, CancellationToken cancellationToken)
    {
        if (id.HasValue && !await context.Catalog.AnyAsync(entry => entry.Id == id && entry.Kind == kind && !entry.Archived, cancellationToken))
            throw new AccountOperationException(400, "invalid_listing_catalog_entry", "Choose an available catalog entry.",
                new Dictionary<string, string[]> { [field] = ["Choose an available entry."] });
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<string?> ReadReviewReasonAsync(ClassifiedsDbContext context, ListingDraft draft, CancellationToken cancellationToken) =>
        draft.ReviewOutcome is not ("Returned" or "Rejected") ? null : await context.ReviewDecisions
            .Where(decision => decision.ListingId == draft.Id && decision.SubmittedVersion < draft.Version)
            .OrderByDescending(decision => decision.SubmittedVersion).Select(decision => decision.Reason).FirstOrDefaultAsync(cancellationToken);

    internal static ListingDraftResponse Response(ListingDraft draft) =>
        new(draft.Id, new ListingDraftInput
        {
            Title = draft.Title,
            Description = draft.Description,
            CategoryId = draft.CategoryId,
            DistrictId = draft.DistrictId,
            Price = draft.Price,
            Phone = draft.Phone,
            Email = draft.Email
        }, draft.UpdatedAtUtc, draft.Version, draft.SubmittedAtUtc.HasValue ? "InReview" : draft.ReviewOutcome ?? "Draft");
}
