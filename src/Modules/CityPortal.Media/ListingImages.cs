using System.Net;
using Amazon.S3;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

/// <summary>Manages private photos after Classifieds checks draft ownership.</summary>
public sealed class ListingImages
{
    private readonly MediaDbContext _context;
    private readonly Func<MediaObjectStore> _storage;

    internal ListingImages(MediaDbContext context, Func<MediaObjectStore> storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task<ImageResponse> GetUploadAsync(Guid ownerId, Guid imageId, CancellationToken cancellationToken)
    {
        MediaImage image = await _context.Images.AsNoTracking().SingleOrDefaultAsync(item => item.Id == imageId
            && item.OwnerId == ownerId && item.ForListing && item.State != "Deleting", cancellationToken) ?? throw MediaDatabase.Missing();
        return MediaOperations.Response(image);
    }

    public async Task<ListingPhotoResponse[]> ReadAsync(Guid draftId, CancellationToken cancellationToken)
    {
        var rows = await (from reference in _context.ListingImages.AsNoTracking()
                          join image in _context.Images.AsNoTracking() on reference.ImageId equals image.Id
                          where reference.DraftId == draftId
                          orderby reference.Position
                          select new { Image = image, reference.AlternativeText }).ToArrayAsync(cancellationToken);
        return rows.Select(row => new ListingPhotoResponse(MediaOperations.Response(row.Image), row.AlternativeText)).ToArray();
    }

    public async Task<Stream> OpenAsync(Guid draftId, Guid imageId, CancellationToken cancellationToken)
    {
        bool ready = await (from reference in _context.ListingImages
                            join image in _context.Images on reference.ImageId equals image.Id
                            where reference.DraftId == draftId && image.Id == imageId && image.State == "Ready"
                            select image.Id).AnyAsync(cancellationToken);
        if (!ready)
            throw MediaDatabase.Missing();
        return await OpenStoredAsync(imageId, cancellationToken);
    }

    private async Task<Stream> OpenStoredAsync(Guid imageId, CancellationToken cancellationToken)
    {
        try
        {
            return await _storage().OpenAsync(imageId, "1280", cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw MediaDatabase.Missing();
        }
    }

    public Task<PublishedListingPhoto[]> ReadPublishedAsync(Guid revisionId, CancellationToken cancellationToken) =>
        _context.PublishedListingImages.AsNoTracking().Where(reference => reference.RevisionId == revisionId)
            .OrderBy(reference => reference.Position).Select(reference => new PublishedListingPhoto(reference.ImageId, reference.AlternativeText))
            .ToArrayAsync(cancellationToken);

    public async Task CopyForPublicationAsync(AuthorizedAccountScope scope, Guid draftId, Guid revisionId, CancellationToken cancellationToken)
    {
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        ListingImageReference[] references = await context.ListingImages.AsNoTracking().Where(reference => reference.DraftId == draftId)
            .OrderBy(reference => reference.ImageId).ToArrayAsync(cancellationToken);
        foreach (ListingImageReference reference in references)
        {
            MediaImage image = await MediaDatabase.LockImageAsync(context, reference.ImageId, cancellationToken);
            if (image.State != "Ready")
                throw new AccountOperationException(409, "listing_photo_unavailable", "A submitted photo is unavailable.");
            context.PublishedListingImages.Add(new PublishedListingImageReference
            {
                RevisionId = revisionId,
                ImageId = reference.ImageId,
                AlternativeText = reference.AlternativeText,
                Position = reference.Position
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Stream> OpenPublishedAsync(Guid revisionId, Guid imageId, CancellationToken cancellationToken)
    {
        if (!await _context.PublishedListingImages.AnyAsync(reference => reference.RevisionId == revisionId && reference.ImageId == imageId, cancellationToken))
            throw MediaDatabase.Missing();
        return await OpenStoredAsync(imageId, cancellationToken);
    }

    public async Task ReleasePublicationAsync(AuthorizedAccountScope scope, Guid revisionId, CancellationToken cancellationToken)
    {
        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        PublishedListingImageReference[] references = await context.PublishedListingImages.Where(item => item.RevisionId == revisionId)
            .OrderBy(item => item.ImageId).ToArrayAsync(cancellationToken);
        foreach (PublishedListingImageReference reference in references)
        {
            MediaImage image = await MediaDatabase.LockImageAsync(context, reference.ImageId, cancellationToken);
            context.PublishedListingImages.Remove(reference);
            if (!await context.ListingImages.AnyAsync(item => item.ImageId == image.Id, cancellationToken)
                && !await context.PublishedListingImages.AnyAsync(item => item.ImageId == image.Id && item.RevisionId != revisionId, cancellationToken))
            {
                image.State = "Deleting";
                image.Version++;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ListingPhotoResponse[]> ReplaceAsync(AuthorizedAccountScope scope, Guid draftId, ListingPhotoInput[] photos, CancellationToken cancellationToken)
    {
        if (photos.Length > 5 || photos.Any(photo => photo is null || photo.ImageId == Guid.Empty
            || string.IsNullOrWhiteSpace(photo.AlternativeText) || photo.AlternativeText.Length > 300 || photo.AlternativeText.Contains('\0')))
            throw InvalidSelection();

        var selected = new Dictionary<Guid, (int Position, string AlternativeText)>();
        for (int index = 0; index < photos.Length; index++)
            if (!selected.TryAdd(photos[index].ImageId, (index, photos[index].AlternativeText.Trim())))
                throw InvalidSelection();

        await using MediaDbContext context = await MediaDatabase.JoinAsync(scope.Connection, scope.Transaction, cancellationToken);
        Dictionary<Guid, ListingImageReference> existing = await context.ListingImages.Where(reference => reference.DraftId == draftId)
            .ToDictionaryAsync(reference => reference.ImageId, cancellationToken);
        var result = new ListingPhotoResponse[photos.Length];
        foreach (Guid id in selected.Keys.Union(existing.Keys).Order())
        {
            MediaImage image = await MediaDatabase.LockImageAsync(context, id, cancellationToken);
            ListingImageReference? reference = existing.GetValueOrDefault(id);
            if (!selected.TryGetValue(id, out (int Position, string AlternativeText) photo))
            {
                if (reference is not null)
                    context.ListingImages.Remove(reference);
                if (!await context.PublishedListingImages.AnyAsync(item => item.ImageId == id, cancellationToken))
                {
                    image.State = "Deleting";
                    image.Version++;
                }
                continue;
            }

            if (!image.ForListing || image.OwnerId != scope.Actor.Id || image.State == "Deleting")
                throw MediaDatabase.Missing();
            if (reference is null)
            {
                if (await context.ListingImages.AnyAsync(item => item.ImageId == id, cancellationToken))
                    throw MediaDatabase.Missing();
                reference = new ListingImageReference { DraftId = draftId, ImageId = id };
                context.ListingImages.Add(reference);
            }
            reference.Position = photo.Position;
            reference.AlternativeText = photo.AlternativeText;
            result[photo.Position] = new ListingPhotoResponse(MediaOperations.Response(image), photo.AlternativeText);
        }

        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static AccountOperationException InvalidSelection() =>
        new(400, "invalid_listing_photos", "Select up to five distinct photos with alternative text.");
}
