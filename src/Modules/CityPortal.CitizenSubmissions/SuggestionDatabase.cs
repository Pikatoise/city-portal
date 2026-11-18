using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.CitizenSubmissions;

internal static class SuggestionDatabase
{
    internal static IQueryable<Suggestion> Active(SuggestionsDbContext context, DateTimeOffset now) =>
        context.Suggestions.Where(item => item.PausedAtUtc != null || item.ExpiresAtUtc > now);

    internal static async Task<SuggestionsDbContext> JoinAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var context = new SuggestionsDbContext(new DbContextOptionsBuilder<SuggestionsDbContext>().UseNpgsql(connection).Options);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return context;
    }
}
