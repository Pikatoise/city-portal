using System.Data.Common;

namespace CityPortal.News;

/// <summary>Keeps a linked workflow consistent when its unpublished article is deleted.</summary>
public interface IArticleDraftDeletionObserver
{
    Task DraftDeletedAsync(Guid articleId, DateTimeOffset deletedAtUtc, DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken);
}
