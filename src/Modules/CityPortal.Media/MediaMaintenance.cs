using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace CityPortal.Media;

public sealed class MediaMaintenance
{
    private readonly MediaDbContext _context;
    private readonly MediaObjectStore _storage;
    private readonly TimeProvider _time;
    private readonly ILogger<MediaMaintenance> _logger;

    internal MediaMaintenance(MediaDbContext context, MediaObjectStore storage, TimeProvider time, ILogger<MediaMaintenance> logger)
    {
        _context = context;
        _storage = storage;
        _time = time;
        _logger = logger;
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        Guid[] candidates = await _context.Images.AsNoTracking().Where(image => image.State == "Processing" && image.NextAttemptAtUtc <= now).OrderBy(image => image.CreatedAtUtc).Select(image => image.Id).Take(20).ToArrayAsync(cancellationToken);
        foreach (Guid id in candidates)
        {
            _context.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            MediaImage image = await MediaDatabase.LockImageAsync(_context, id, cancellationToken);
            if (image.State != "Processing" || image.NextAttemptAtUtc > _time.GetUtcNow())
                continue;
            ImageOutput[] outputs = await _context.Outputs.Where(output => output.ImageId == id).ToArrayAsync(cancellationToken);
            try
            {
                if (outputs.Length != 4 || outputs.Any(output => output.PendingBytes is null))
                {
                    image.State = "Failed";
                    image.ErrorCode = "processing_payload_missing";
                }
                else
                {
                    foreach (ImageOutput output in outputs)
                        await _storage.PutAsync(id, output.Variant, output.PendingBytes!, cancellationToken);
                    image.State = "Ready";
                    image.ErrorCode = null;
                    foreach (ImageOutput output in outputs)
                        output.PendingBytes = null;
                }
            }
            catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or IOException or TimeoutException)
            {
                image.Attempts++;
                image.NextAttemptAtUtc = _time.GetUtcNow().AddSeconds(Math.Min(900, 10 * Math.Pow(2, Math.Min(image.Attempts, 7))));
                image.ErrorCode = "storage_unavailable";
                _logger.LogWarning("Media storage write deferred for {ImageId}; attempt {Attempt}.", id, image.Attempts);
            }
            image.Version++;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = _time.GetUtcNow().AddDays(-7);
        Guid[] candidates = await _context.Images.AsNoTracking().Where(image => image.State == "Deleting"
            || (!image.EverAttached || image.ForSuggestion && !_context.References.Any(item => item.ImageId == image.Id))
            && image.CreatedAtUtc <= cutoff && !_context.SuggestionImages.Any(item => item.ImageId == image.Id)
            && !_context.ListingImages.Any(item => item.ImageId == image.Id)
            && !_context.PublishedListingImages.Any(item => item.ImageId == image.Id))
            .OrderBy(image => image.CreatedAtUtc).Select(image => image.Id).Take(50).ToArrayAsync(cancellationToken);
        foreach (Guid id in candidates)
        {
            _context.ChangeTracker.Clear();
            await using (IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
            {
                MediaImage? image = (await _context.Images.FromSqlInterpolated($"SELECT * FROM media.\"Images\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
                if (image is null || image.EverAttached && !image.ForSuggestion || image.State != "Deleting" && image.CreatedAtUtc > cutoff
                    || await _context.SuggestionImages.AnyAsync(reference => reference.ImageId == id, cancellationToken)
                    || await _context.PublishedListingImages.AnyAsync(reference => reference.ImageId == id, cancellationToken)
                    || await _context.ListingImages.AnyAsync(reference => reference.ImageId == id, cancellationToken)
                    || await _context.References.AnyAsync(reference => reference.ImageId == id, cancellationToken)
                    || await _context.Events.AnyAsync(entry => entry.ImageId == id, cancellationToken))
                    continue;
                image.State = "Deleting";
                image.Version++;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            try
            {
                await _storage.DeleteAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or IOException or TimeoutException)
            {
                _logger.LogWarning("Media object deletion deferred for {ImageId}.", id);
                continue;
            }
            await _context.Outputs.Where(output => output.ImageId == id).ExecuteDeleteAsync(cancellationToken);
            await _context.Images.Where(image => image.Id == id && image.State == "Deleting").ExecuteDeleteAsync(cancellationToken);
        }
    }
}
