using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

internal static class ClassifiedsDatabase
{
    internal const int PageSize = 20;

    internal static int Offset(int page)
    {
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");
        return (page - 1) * PageSize;
    }

    internal static IQueryable<ListingPublication> Visible(ClassifiedsDbContext context, DateTimeOffset now) =>
        context.Publications.AsNoTracking().Where(item => !item.HiddenByAccountBlock && !item.HiddenByListingBlock && item.ExpiresAtUtc > now);

    internal static async Task<ClassifiedsDbContext> JoinAsync(AuthorizedAccountScope scope, CancellationToken cancellationToken)
    {
        var context = new ClassifiedsDbContext(new DbContextOptionsBuilder<ClassifiedsDbContext>().UseNpgsql(scope.Connection).Options);
        try
        {
            await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}
