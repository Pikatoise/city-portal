using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.Media;

internal sealed class MediaDesignTimeFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public MediaDbContext CreateDbContext(string[] args)
    {
        string database = Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=cityportal";
        return new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(database, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "media")).Options);
    }
}
