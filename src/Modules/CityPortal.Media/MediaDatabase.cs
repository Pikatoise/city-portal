using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

internal static class MediaDatabase
{
    internal static async Task<MediaDbContext> JoinAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var context = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(connection).Options);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return context;
    }

    internal static async Task<MediaImage> LockImageAsync(MediaDbContext context, Guid id, CancellationToken cancellationToken) =>
        (await context.Images.FromSqlInterpolated($"SELECT * FROM media.\"Images\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();

    internal static CityPortal.Identity.AccountOperationException Missing() => new(404, "media_not_found", "The requested media is unavailable.");

    internal static CityPortal.Identity.AccountOperationException Conflict() => new(409, "media_conflict", "The item changed or this action is no longer available. Reload it.");

    internal static void Page(int page, int pageSize)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
            throw new CityPortal.Identity.AccountOperationException(400, "invalid_page", "Choose a valid page and page size from 1 to 100.");
    }
}
