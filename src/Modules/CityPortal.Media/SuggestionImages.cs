using System.Data.Common;
using System.Net;
using Amazon.S3;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

/// <summary>Manages private image attachments after Citizen Submissions has checked suggestion access.</summary>
public sealed class SuggestionImages
{
    private readonly MediaDbContext _context;
    private readonly Func<MediaObjectStore> _storage;

    internal SuggestionImages(MediaDbContext context, Func<MediaObjectStore> storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task<ImageResponse[]> ReadAsync(Guid suggestionId, CancellationToken cancellationToken)
    {
        MediaImage[] images = await (from reference in _context.SuggestionImages.AsNoTracking()
                                     join image in _context.Images on reference.ImageId equals image.Id
                                     where reference.SuggestionId == suggestionId
                                     orderby image.CreatedAtUtc, image.Id
                                     select image).ToArrayAsync(cancellationToken);
        return images.Select(MediaOperations.Response).ToArray();
    }

    public async Task<Stream> OpenAsync(Guid suggestionId, Guid imageId, CancellationToken cancellationToken)
    {
        bool ready = await (from reference in _context.SuggestionImages
                            join image in _context.Images on reference.ImageId equals image.Id
                            where reference.SuggestionId == suggestionId && image.Id == imageId && image.State == "Ready"
                            select image.Id).AnyAsync(cancellationToken);
        if (!ready)
            throw MediaDatabase.Missing();
        try
        {
            return await _storage().OpenAsync(imageId, "1280", cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // A committed detach and cleanup can remove the object after the reference check.
            throw MediaDatabase.Missing();
        }
    }

    public async Task RequireReadyAsync(Guid suggestionId, Guid[] ids, CancellationToken cancellationToken)
    {
        int found = await (from reference in _context.SuggestionImages
                           join image in _context.Images on reference.ImageId equals image.Id
                           where reference.SuggestionId == suggestionId && ids.Contains(image.Id) && image.State == "Ready"
                           select image.Id).CountAsync(cancellationToken);
        if (found != ids.Length)
            throw MediaDatabase.Conflict();
    }

    public async Task<ImageResponse[]> ReplaceAsync(Guid suggestionId, Guid residentId, Guid[] ids, DbConnection connection,
        DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (ids.Length > 5 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new AccountOperationException(400, "invalid_suggestion_images", "Select at most five distinct images.");

        await using MediaDbContext context = await MediaDatabase.JoinAsync(connection, transaction, cancellationToken);
        SuggestionImageReference[] existing = await context.SuggestionImages.Where(item => item.SuggestionId == suggestionId).ToArrayAsync(cancellationToken);
        if (ids.Length == 0 && existing.Length == 0)
            return [];

        var result = new List<ImageResponse>(ids.Length);
        foreach (Guid id in ids.Union(existing.Select(item => item.ImageId)).Order())
        {
            MediaImage image = await MediaDatabase.LockImageAsync(context, id, cancellationToken);
            SuggestionImageReference? previous = existing.SingleOrDefault(item => item.ImageId == id);
            if (!ids.Contains(id))
            {
                if (previous is not null)
                    context.SuggestionImages.Remove(previous);
                if (!await context.References.AnyAsync(item => item.ImageId == id, cancellationToken))
                {
                    image.State = "Deleting";
                    image.Version++;
                }
                continue;
            }
            if (!image.ForSuggestion || image.OwnerId != residentId || image.State == "Deleting")
                throw MediaDatabase.Missing();

            if (previous is null)
            {
                if (await context.SuggestionImages.AnyAsync(item => item.ImageId == id, cancellationToken))
                    throw MediaDatabase.Missing();
                context.SuggestionImages.Add(new SuggestionImageReference { ImageId = id, SuggestionId = suggestionId });
            }
            result.Add(MediaOperations.Response(image));
        }
        await context.SaveChangesAsync(cancellationToken);
        return result.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToArray();
    }
}
