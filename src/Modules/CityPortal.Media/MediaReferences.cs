using System.Data.Common;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

/// <summary>News calls this operation inside its authorized publication/revision transaction.</summary>
public sealed class MediaReferences
{
    public async Task ReplaceAsync(DbConnection connection, DbTransaction transaction, Guid revisionId, Guid authorId, Guid? reviewerId, bool submitted, bool isPublic, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken, Guid? inheritedRevisionId = null, bool isCurrent = true)
    {
        Guid[] ids = imageIds.Distinct().Order().ToArray();
        if (ids.Length > 20 || revisionId == Guid.Empty || authorId == Guid.Empty || isPublic && (!submitted || !isCurrent))
            throw new AccountOperationException(400, "invalid_media_references", "A revision may reference at most 20 distinct images and must have a valid owner.");
        await using MediaDbContext context = await MediaDatabase.JoinAsync(connection, transaction, cancellationToken);
        // Serialize concurrent replacements of the same revision as well as cleanup of individual images.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"media-revision:" + revisionId}, 0))", cancellationToken);
        ImageReference[] existing = await context.References.Where(reference => reference.RevisionId == revisionId).ToArrayAsync(cancellationToken);
        foreach (Guid id in ids.Union(existing.Select(reference => reference.ImageId)).Order())
        {
            MediaImage image = await MediaDatabase.LockImageAsync(context, id, cancellationToken);
            if (!ids.Contains(id))
                continue;
            if (image.State != "Ready")
                throw MediaDatabase.Conflict();
            // News validates that the inherited revision belongs to this same article before calling.
            bool retained = existing.Any(reference => reference.ImageId == id) || inheritedRevisionId.HasValue && await context.References.AnyAsync(reference => reference.RevisionId == inheritedRevisionId && reference.AuthorId == authorId && reference.ImageId == id, cancellationToken);
            bool selectable = image.OwnerId == authorId || await context.Templates.AnyAsync(template => template.ImageId == id && template.Approval == "Approved" && !template.Archived && !template.Blocked, cancellationToken);
            bool blocked = await context.Templates.AnyAsync(template => template.ImageId == id && template.Blocked, cancellationToken);
            if (!retained && (!selectable || blocked))
                throw MediaDatabase.Missing();
            image.EverAttached = true;
        }
        context.References.RemoveRange(existing);
        await context.SaveChangesAsync(cancellationToken);
        context.References.AddRange(ids.Select(id => new ImageReference { RevisionId = revisionId, ImageId = id, AuthorId = authorId, ReviewerId = reviewerId, Submitted = submitted, Public = isPublic, Current = isCurrent }));
        await context.SaveChangesAsync(cancellationToken);
    }
}
