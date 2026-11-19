using System.Data.Common;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

/// <summary>News calls this operation inside its authorized publication/revision transaction.</summary>
public sealed class MediaReferences
{
    private const int _visibilityBatchSize = 128;

    public async Task ReplaceAsync(DbConnection connection, DbTransaction transaction, Guid revisionId, Guid authorId, Guid? reviewerId, bool submitted, bool isPublic, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken, Guid? inheritedRevisionId = null, Guid? selectionOwnerId = null, Guid? suggestionId = null)
    {
        Guid[] ids = imageIds.Distinct().Order().ToArray();
        if (ids.Length > 20 || revisionId == Guid.Empty || authorId == Guid.Empty || isPublic && !submitted)
            throw new AccountOperationException(400, "invalid_media_references", "A revision may reference at most 20 distinct images and must have a valid owner.");
        await using MediaDbContext context = await MediaDatabase.JoinAsync(connection, transaction, cancellationToken);
        // Serialize concurrent replacements of the same revision as well as cleanup of individual images.
        await LockRevisionsAsync(context, [revisionId], cancellationToken);
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
            bool selectable = image.ForSuggestion
                ? suggestionId.HasValue && await context.SuggestionImages.AnyAsync(item => item.ImageId == id && item.SuggestionId == suggestionId, cancellationToken)
                : image.OwnerId == (selectionOwnerId ?? authorId) || await context.Templates.AnyAsync(template => template.ImageId == id && template.Approval == "Approved" && !template.Archived && !template.Blocked, cancellationToken);
            bool blocked = await context.Templates.AnyAsync(template => template.ImageId == id && template.Blocked, cancellationToken);
            if (!retained && (!selectable || blocked))
                throw MediaDatabase.Missing();
            image.EverAttached = true;
        }
        context.References.RemoveRange(existing);
        await context.SaveChangesAsync(cancellationToken);
        context.References.AddRange(ids.Select(id => new ImageReference { RevisionId = revisionId, ImageId = id, AuthorId = authorId, ReviewerId = reviewerId, Submitted = submitted, Public = isPublic }));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateVisibilityAsync(DbConnection connection, DbTransaction transaction,
        IReadOnlyCollection<RevisionMediaVisibility> revisions, CancellationToken cancellationToken)
    {
        if (revisions.Any(revision => revision.IsPublic && (!revision.Submitted || !revision.IsCurrent)))
            throw new AccountOperationException(400, "invalid_media_references", "Public references require a current submitted revision.");

        await using MediaDbContext context = await MediaDatabase.JoinAsync(connection, transaction, cancellationToken);
        foreach (RevisionMediaVisibility[] batch in revisions.OrderBy(revision => revision.RevisionId).Chunk(_visibilityBatchSize))
        {
            Guid[] revisionIds = batch.Select(revision => revision.RevisionId).ToArray();
            await LockRevisionsAsync(context, revisionIds, cancellationToken);
            // Take the same image row locks used for selection, template decisions and cleanup.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT image."Id" FROM media."Images" image
                WHERE image."Id" IN (
                    SELECT reference."ImageId" FROM media."References" reference WHERE reference."RevisionId" = ANY({revisionIds})
                )
                ORDER BY image."Id" FOR UPDATE
                """, cancellationToken);

            foreach (var group in batch.GroupBy(revision => new
            { revision.ReviewerId, revision.Submitted, revision.IsPublic, revision.IsCurrent, revision.AuthorVisible }))
            {
                Guid[] ids = group.Select(revision => revision.RevisionId).ToArray();
                var audience = group.Key;
                await context.References
                    .Where(reference => ids.Contains(reference.RevisionId) &&
                        (reference.ReviewerId != audience.ReviewerId || reference.Submitted != audience.Submitted ||
                         reference.Public != audience.IsPublic || reference.Current != audience.IsCurrent ||
                         reference.AuthorVisible != audience.AuthorVisible))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(reference => reference.ReviewerId, audience.ReviewerId)
                        .SetProperty(reference => reference.Submitted, audience.Submitted)
                        .SetProperty(reference => reference.Public, audience.IsPublic)
                        .SetProperty(reference => reference.Current, audience.IsCurrent)
                        .SetProperty(reference => reference.AuthorVisible, audience.AuthorVisible), cancellationToken);
            }
        }
    }

    // PostgreSQL's UUID text matches the previous single-revision lock key.
    private static Task LockRevisionsAsync(MediaDbContext context, Guid[] revisionIds, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended('media-revision:' || revision.id::text, 0))
            FROM unnest({revisionIds}) AS revision(id) ORDER BY revision.id
            """, cancellationToken);
}
