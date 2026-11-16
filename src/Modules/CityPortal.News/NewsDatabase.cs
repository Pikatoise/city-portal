using System.Data.Common;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

internal static class NewsDatabase
{
    internal static async Task<NewsDbContext> JoinAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var context = new NewsDbContext(new DbContextOptionsBuilder<NewsDbContext>().UseNpgsql(connection).Options);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return context;
    }

    internal static AccountOperationException Missing() => new(404, "article_not_found", "The article or taxonomy entry is unavailable.");
    internal static AccountOperationException Conflict() => new(409, "article_conflict", "The item changed. Preserve unsaved edits and reload the current version.");
    internal static AccountOperationException Invalid(string field, string message) => new(400, "invalid_article", message, new Dictionary<string, string[]> { [field] = [message] });

    internal static void Page(int page, int pageSize)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw Invalid("page", "Choose a valid page and a page size from 1 to 100.");
    }
}
