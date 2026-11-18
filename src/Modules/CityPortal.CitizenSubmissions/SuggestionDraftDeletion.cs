using System.Data.Common;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.CitizenSubmissions;

internal sealed class SuggestionDraftDeletion : IArticleDraftDeletionObserver
{
    public async Task DraftDeletedAsync(Guid articleId, DateTimeOffset deletedAtUtc, DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SuggestionsDbContext context = await SuggestionDatabase.JoinAsync(connection, transaction, cancellationToken);
        Suggestion? item = await context.Suggestions.SingleOrDefaultAsync(value => value.ArticleId == articleId, cancellationToken);
        if (item?.PausedAtUtc is not DateTimeOffset paused)
            return;

        TimeSpan pause = deletedAtUtc > paused ? deletedAtUtc - paused : TimeSpan.Zero;
        item.ExpiresAtUtc = item.ExpiresAtUtc.Add(pause);
        item.AssigneeId = null;
        item.PausedAtUtc = null;
        item.ArticleId = null;
        item.Version++;
        await context.SaveChangesAsync(cancellationToken);
    }
}
