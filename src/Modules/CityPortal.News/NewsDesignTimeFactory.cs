using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.News;

internal sealed class NewsDesignTimeFactory : IDesignTimeDbContextFactory<NewsDbContext>
{
    public NewsDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=cityportal";
        var options = new DbContextOptionsBuilder<NewsDbContext>();
        options.UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "news"));
        return new NewsDbContext(options.Options);
    }
}
